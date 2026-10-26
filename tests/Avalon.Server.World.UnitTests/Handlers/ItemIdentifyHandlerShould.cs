using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Server.World.UnitTests.Doodads;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.Seeding;
using Avalon.World;
using Avalon.World.Configuration;
using Avalon.World.Doodads;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Inventory;
using Avalon.World.Persistence;
using Avalon.World.Public.Characters;
using Avalon.World.Testing.Scenarios;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;
using static Avalon.Server.World.UnitTests.Inventory.TestModifiers;

namespace Avalon.Server.World.UnitTests.Handlers;

/// <summary>
/// CMSG_ITEM_IDENTIFY at the Resonance Workbench (#1179), through the real handler, a real instance with the seeded
/// Workbench placed in it, the seeded templates and catalog, and the economy's own wallet and inventory service: every
/// request gets one SMSG_ITEM_IDENTIFY_RESULT, a refusal names the first rule it breaks and changes nothing, and an
/// identified item reaches the client with its name and every modifier in the next inventory update. Character id
/// 117_901 is this class's.
/// </summary>
public class ItemIdentifyHandlerShould
{
    private static readonly StaticData s_data = ScenarioReferenceData.Shared.Data;
    private static readonly ItemTemplate s_chest = s_data.Items.Templates.Single(t => t.Name == "Ironbound Chestguard");

    private static readonly DoodadTemplateView s_workbench =
        s_data.Doodads.Templates.Single(t => t.Name == "Resonance Workbench");

    private static readonly DoodadTemplateView s_forestChest = s_data.Doodads.Templates.Single(t => t.Name == "Forest Chest");

    private const uint Me = 117_901, RequestId = 41;
    private const byte Slot = 3;

    // The Workbench stands at x 10, and the character at x 6, 4 m from it, within its 5 m.
    private static readonly Vector3 s_atWorkbench = new(10f, 0f, 0f), s_beside = new(6f, 0f, 0f);

    // The rules in the order IdentifyRules asks them. A row breaks its own rule and every one after it but the gold
    // (which costs nothing unless the row says so), so it shows each earlier rule is asked first.
    private static readonly ItemIdentifyResult[] s_order =
    [
        ItemIdentifyResult.Dead, ItemIdentifyResult.Stunned, ItemIdentifyResult.NotFound, ItemIdentifyResult.TooFar,
        ItemIdentifyResult.NoItem, ItemIdentifyResult.AlreadyIdentified, ItemIdentifyResult.Locked,
        ItemIdentifyResult.NotEnoughGold,
    ];

    [Theory]
    [InlineData("dead", ItemIdentifyResult.Dead)]
    [InlineData("stunned", ItemIdentifyResult.Stunned)]
    [InlineData("unknown guid", ItemIdentifyResult.NotFound)]
    [InlineData("a chest's guid", ItemIdentifyResult.NotFound)]
    [InlineData("despawned workbench", ItemIdentifyResult.NotFound)]
    [InlineData("5.01 m away", ItemIdentifyResult.TooFar)]
    [InlineData("position NaN", ItemIdentifyResult.TooFar)]
    [InlineData("slot 30", ItemIdentifyResult.NoItem)]
    [InlineData("empty slot", ItemIdentifyResult.NoItem)]
    [InlineData("identified item", ItemIdentifyResult.AlreadyIdentified)]
    [InlineData("offered in a trade", ItemIdentifyResult.Locked)]
    [InlineData("cost 10, 9 gold", ItemIdentifyResult.NotEnoughGold)]
    [InlineData("cost 10, 12 gold with 5 held for a trade", ItemIdentifyResult.NotEnoughGold)]
    public void Refuse_an_identify_naming_why_in_order(string @case, ItemIdentifyResult expected)
    {
        bool gold = expected == ItemIdentifyResult.NotEnoughGold;
        bool held = @case == "cost 10, 12 gold with 5 held for a trade";
        using var f = new Fixture(identifyCost: gold ? 10UL : 0UL, gold: held ? 12UL : 9UL,
            identified: Breaks(ItemIdentifyResult.AlreadyIdentified));
        CharacterEntity character = f.Client.Character;
        InventoryItem item = TestCharacters.At(character, InventoryType.Bag, Slot);

        if (Breaks(ItemIdentifyResult.Locked))
            character.Locks.Lock(item.InstanceId);
        if (held)
            character.Locks.HeldGold = 5;

        byte bagSlot = Breaks(ItemIdentifyResult.NoItem) ? @case == "empty slot" ? (byte)2 : (byte)30 : Slot;

        if (Breaks(ItemIdentifyResult.TooFar))
            character.Position = @case == "position NaN" ? new Vector3(float.NaN, 0f, 0f) : new Vector3(4.99f, 0f, 0f);

        ObjectGuid workbench = f.Workbench.Guid;
        if (Breaks(ItemIdentifyResult.NotFound))
        {
            switch (@case)
            {
                case "a chest's guid":
                    workbench = f.Chest.Guid;
                    break;
                case "despawned workbench":
                    // Nothing despawns an Unlimited, script-less Workbench; the rule is asked all the same.
                    DoodadStates.Set(f.Workbench, DoodadState.Despawned);
                    break;
                default:
                    workbench = new ObjectGuid(ObjectType.Doodad, 999);
                    break;
            }
        }

        if (Breaks(ItemIdentifyResult.Stunned))
            character.Auras.Add(Auras.AuraTestData.HeldControl(CrowdControl.Stun), DateTimeOffset.UnixEpoch);
        if (Breaks(ItemIdentifyResult.Dead))
            character.IsDead = true;

        ulong goldBefore = character.Data!.Gold;

        Assert.Equal(expected, f.Identify(workbench, bagSlot));

        Assert.Equal(item.Identified, TestCharacters.At(character, InventoryType.Bag, Slot).Identified);
        Assert.Equal(goldBefore, character.Data.Gold);
        Assert.Equal(SaveState.Unchanged, character.SaveState.ItemState(item.InstanceId));
        Assert.False(character.SaveState.GoldDirty);
        Assert.False(character.ClientChanges.HasChanges);

        bool Breaks(ItemIdentifyResult rule) => Array.IndexOf(s_order, rule) >= Array.IndexOf(s_order, expected)
            && (rule != ItemIdentifyResult.NotEnoughGold || gold);
    }

    /// <summary>
    /// Identifying changes no modifier and no items generation, so a name held from a describe while it was unidentified
    /// would stay null: the next update must name it.
    /// </summary>
    [Theory]
    [InlineData(0UL, 0UL, 0UL)]
    [InlineData(10UL, 25UL, 15UL)]
    public void Identify_an_item_and_reveal_it_in_the_next_inventory_update(ulong identifyCost, ulong gold, ulong goldAfter)
    {
        using var f = new Fixture(identifyCost, gold, identified: false);
        CharacterEntity character = f.Client.Character;
        ItemInstanceId id = TestCharacters.At(character, InventoryType.Bag, Slot).InstanceId;
        ItemSlotDto first = SlotIn(Assert.Single(f.Updates()));
        Assert.Equal((false, null), (first.Identified, first.Name));
        Assert.Equal([CatalogKeys.ModifierId("implicit IncreasedMaximumLife T9")],
            first.Modifiers!.Select(m => (int)m.ModifierId));

        Assert.Equal(ItemIdentifyResult.Ok, f.Identify(f.Workbench.Guid, Slot));
        SaveMark mark = character.SaveState.TakeMarks().Items[id];
        f.Flush();

        SInventoryUpdatePacket update = f.Updates()[1];
        ItemSlotDto revealed = SlotIn(update);
        Assert.Equal((true, "Hale Ironbound Chestguard of Deep Roots"), (revealed.Identified, revealed.Name));
        Assert.Equal(
            [
                CatalogKeys.ModifierId("implicit IncreasedMaximumLife T9"), CatalogKeys.ModifierId("P11 T7"),
                CatalogKeys.ModifierId("S14 T9"),
            ],
            revealed.Modifiers!.Select(m => (int)m.ModifierId));
        Assert.Equal((SaveState.Changed, false), (mark.State, mark.Modifiers));
        Assert.True(TestCharacters.At(character, InventoryType.Bag, Slot).Identified);
        Assert.Equal(goldAfter, character.Data!.Gold);
        Assert.Equal(identifyCost > 0 ? goldAfter : null, update.Gold);
    }

    private static ItemSlotDto SlotIn(SInventoryUpdatePacket update) =>
        Assert.Single(update.Slots, s => s.Container == InventoryType.Bag && s.Slot == Slot).Item!;

    /// <summary>
    /// The instance with the seeded Workbench at x 10 and a forest chest (doodad 1) beside it, the character 4 m from the
    /// Workbench with an unidentified (unless asked otherwise) Magic Ironbound Chestguard at item level 9 in Bag slot 3,
    /// already sent once and saved, so a request starts from a character with nothing to save or send.
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        private readonly ItemModifierDtoPool _pool = new();

        public Fixture(ulong identifyCost, ulong gold, bool identified)
        {
            IWorld world = NewWorld(s_data);
            world.Configuration.Returns(new GameConfiguration { Items = new ItemConfiguration { IdentifyCost = identifyCost } });
            Instance = TestMapInstances.Build(world, time: new FakeTimeProvider());
            world.InstanceRegistry.GetInstanceById(Instance.InstanceId).Returns(Instance);
            Workbench = Place(2, s_workbench, s_atWorkbench);
            Chest = Place(1, s_forestChest, new Vector3(8f, 0f, 2f));

            CharacterEntity character = TestCharacters.New(Me, gold);
            character.Position = s_beside;
            Client = Join(Instance, character);

            CharacterEconomy economy = TestEconomy.Over(world);
            Handler = new ItemIdentifyHandler(NullLogger<ItemIdentifyHandler>.Instance, world, economy);
            Assert.Equal(InventoryAddResult.Ok, economy.InventoryOf(character).TryAddInstanceAt(Chestguard(identified), Slot));

            // The tick that sends it, and the save that writes it.
            Flush();
            character.SaveState.Acknowledge(character.SaveState.TakeMarks());
        }

        public MapInstance Instance { get; }
        public MapInstanceClient Client { get; }
        public Doodad Workbench { get; }
        public Doodad Chest { get; }
        public ItemIdentifyHandler Handler { get; }

        /// <summary>One request, and the one answer it got, its request id echoed.</summary>
        public ItemIdentifyResult Identify(ObjectGuid workbench, byte bagSlot)
        {
            int before = Results().Count;
            Handler.Execute(Client.Connection,
                new CItemIdentifyPacket { RequestId = RequestId, WorkbenchGuid = workbench.RawValue, BagSlot = bagSlot });

            List<SItemIdentifyResultPacket> results = Results();
            Assert.Equal(before + 1, results.Count);
            Assert.Equal(RequestId, results[^1].RequestId);
            return results[^1].Result;
        }

        /// <summary>The tick's inventory flush, over the current items generation.</summary>
        public void Flush() => InventoryUpdateFlusher.Flush(Client.Connection, s_data.Items, _pool);

        public List<SInventoryUpdatePacket> Updates() =>
            Client.Read<SInventoryUpdatePacket>(NetworkPacketType.SMSG_INVENTORY_UPDATE);

        public void Dispose() => Instance.Dispose();

        private List<SItemIdentifyResultPacket> Results() =>
            Client.Read<SItemIdentifyResultPacket>(NetworkPacketType.SMSG_ITEM_IDENTIFY_RESULT);

        private Doodad Place(uint id, DoodadTemplateView template, Vector3 position)
        {
            var doodad = new Doodad(new ObjectGuid(ObjectType.Doodad, id), template, position, 0f);
            Instance.AddDoodad(doodad);
            return doodad;
        }

        /// <summary>A Magic Ironbound Chestguard at item level 9: its implicit, "P11 T7" and "S14 T9", and its rolled armour.</summary>
        private static CarriedItem Chestguard(bool identified)
        {
            var id = new ItemInstanceId(Guid.CreateVersion7());
            var item = new InventoryItem(Slot, id, s_chest.Id, 1, 0, ItemInstanceFlags.None, ItemRarity.Magic, 9, null,
                identified, Tainted: false);
            return new CarriedItem(item, new ItemBaseStats { Armour = 8 },
            [
                Modifier(id, CatalogKeys.ModifierId("implicit IncreasedMaximumLife T9"), 0,
                    (0, StatType.IncreasedMaximumLife, 2, null)),
                Modifier(id, CatalogKeys.ModifierId("P11 T7"), 1, (0, StatType.Health, 5, null)),
                Modifier(id, CatalogKeys.ModifierId("S14 T9"), 2, (0, StatType.IncreasedMaximumLife, 1, null)),
            ], 0);
        }
    }
}
