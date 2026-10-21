using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auras;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Loot;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.ItemUse;
using Avalon.World.Auras;
using Avalon.World.Doodads;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Inventory;
using Avalon.World.Loot;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Auras;

/// <summary>What a stunned character may still do (#770), through each action's own handler or service.</summary>
public class CrowdControlGatesShould
{
    private static readonly DateTimeOffset s_now = DateTimeOffset.UnixEpoch;

    /// <summary>
    /// A stunned character uses no item, doodad or gear, picks nothing up, talks to no NPC and acts on no open
    /// conversation (a dialogue choice, the bank), each refusal answered with Stunned where the action has a reply and
    /// dropped where it has none, the conversation left open; once the stun is gone the same request is taken. It still
    /// moves items within its bag and cancels its own buffs. Portal entry, vendors and quests are refused the same way
    /// (EnterMapHandlerShould, VendorHandlersShould, QuestAcceptShould).
    /// </summary>
    [Theory]
    [InlineData("item use", "Stunned", "Ok")]
    [InlineData("doodad use", "none", "used")]
    [InlineData("gear drag on", "Stunned", "Ok")]
    [InlineData("destroy worn gear", "Stunned", "Ok")]
    [InlineData("bag-to-bag drag", "Ok", null)]
    [InlineData("loot pickup", "Stunned", "Ok")]
    [InlineData("npc interact", "none", "opened")]
    [InlineData("dialogue choose", "none", "bank opened")]
    [InlineData("bank move", "Stunned", "Ok")]
    [InlineData("aura cancel", "Ok", null)]
    public async Task Block_actions_while_stunned(string action, string whileStunned, string? onceFree)
    {
        (CharacterEntity character, Func<string> act, IDisposable? owned) = await Prepare(action);
        using IDisposable? _ = owned;
        ActiveAura stun = AuraTestData.HeldControl(CrowdControl.Stun);
        character.Auras.Add(stun, s_now);

        Assert.Equal(whileStunned, act());

        if (onceFree is not null)
        {
            character.Auras.Remove(stun, s_now);
            Assert.Equal(onceFree, act());
        }
    }

    /// <summary>The character, the action (answering what came of it), and what to dispose of afterwards.</summary>
    private static async Task<(CharacterEntity, Func<string>, IDisposable?)> Prepare(string action)
    {
        if (action is "npc interact" or "dialogue choose" or "bank move")
            return Conversation(action, await BankerWorld.CreateAsync());

        ItemUseWorld w = await ItemUseWorld.CreateAsync();
        var economy = new CharacterEconomy(w.World, new ItemIdAllocator());
        uint request = 0;
        Func<string> act;
        switch (action)
        {
            case "item use":
                w.Bag(Item(0, ItemUseWorld.Tonic, count: 5));
                act = () =>
                {
                    w.Use(++request, 0);
                    return w.Results()[^1].Result.ToString();
                };
                break;
            case "doodad use":
                Doodad barrel = Barrel();
                w.Instance.AddDoodad(barrel);
                var scripts = new DoodadScripts(new ServiceCollection().BuildServiceProvider(), w.Time,
                    NullLogger<DoodadScripts>.Instance);
                var doodads = new DoodadUseService(scripts, new DoodadVisibility(scripts), w.World, w.Time,
                    NullLogger<DoodadUseService>.Instance);
                act = () =>
                {
                    doodads.Interact(w.Client.Connection, w.Character, w.Instance, barrel.Guid);
                    return barrel.State == DoodadState.Ready ? "none" : "used";
                };
                break;
            case "gear drag on" or "bag-to-bag drag":
                w.Bag(Item(0, EquipTemplates.Longsword));
                SlotRef to = action == "gear drag on"
                    ? new SlotRef(InventoryType.Equipment, EquipmentSlots.MainHand)
                    : new SlotRef(InventoryType.Bag, 1);
                act = () =>
                {
                    new ItemMoveHandler(NullLogger<ItemMoveHandler>.Instance, w.World, economy)
                        .Execute(w.Client.Connection, new CItemMovePacket
                        {
                            RequestId = ++request,
                            FromContainer = InventoryType.Bag,
                            FromSlot = 0,
                            ToContainer = to.Container,
                            ToSlot = to.Slot,
                        });
                    return ItemResult(w);
                };
                break;
            case "destroy worn gear":
                w.Character.Container(InventoryType.Equipment).Load([Item(EquipmentSlots.MainHand, EquipTemplates.Longsword)]);
                act = () =>
                {
                    new ItemDestroyHandler(NullLogger<ItemDestroyHandler>.Instance, w.World, economy)
                        .Execute(w.Client.Connection, new CItemDestroyPacket
                        {
                            RequestId = ++request,
                            Container = InventoryType.Equipment,
                            Slot = EquipmentSlots.MainHand,
                        });
                    return ItemResult(w);
                };
                break;
            case "loot pickup":
                var drop = new ObjectGuid(ObjectType.Loot, 770_1);
                w.Instance.Drops.Add(new GroundLoot
                {
                    Guid = drop,
                    Position = w.Character.Position,
                    Gold = 25,
                    OwnerCharacterId = 7,
                    FreeForAllAt = w.Time.GetUtcNow().UtcDateTime.AddSeconds(30),
                });
                act = () =>
                {
                    new LootPickupHandler(NullLogger<LootPickupHandler>.Instance, w.World, economy, w.Time)
                        .Execute(w.Client.Connection, new CLootPickupPacket { LootGuid = drop.RawValue });
                    return w.Client.Read<SLootPickupResultPacket>(NetworkPacketType.SMSG_LOOT_PICKUP_RESULT)[^1].Result
                        .ToString();
                };
                break;
            case "aura cancel":
                AuraTemplate fortified = AuraTestData.Fortified();
                w.Character.Auras.Add(new ActiveAura(fortified, w.Character.Guid, AuraSource.None, 1,
                    new AuraSnapshot(0f, 0f, 1), AuraSchedule.Start(s_now, fortified.DurationMs, 0), fortified.DurationMs,
                    s_now.UtcDateTime), s_now);
                act = () =>
                {
                    new AuraCancelHandler(w.World, NullLogger<AuraCancelHandler>.Instance)
                        .Execute(w.Client.Connection, new CAuraCancelPacket { AuraId = fortified.Id.Value });
                    return w.Client.Read<SAuraCancelResultPacket>(NetworkPacketType.SMSG_AURA_CANCEL_RESULT)[^1].Result
                        .ToString();
                };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }

        return (w.Character, act, w);
    }

    /// <summary>The actions on an NPC conversation, by the banker's world: none of them ends it.</summary>
    private static (CharacterEntity, Func<string>, IDisposable?) Conversation(string action, BankerWorld w)
    {
        var economy = new CharacterEconomy(w.World, new ItemIdAllocator());
        uint request = 0;
        Func<string> act;
        switch (action)
        {
            case "npc interact":
                act = () =>
                {
                    new InteractHandler(NullLogger<InteractHandler>.Instance, w.World)
                        .Execute(w.Connection, new CInteractPacket { TargetGuid = BankerWorld.BankerGuid.RawValue });
                    return w.Connection.CurrentDialogue is null ? "none" : "opened";
                };
                break;
            case "dialogue choose":
                w.Connection.CurrentDialogue = (BankerWorld.BankerGuid, new DialogueNodeId(BankerWorld.BankerRoot));
                act = () =>
                {
                    new DialogueChooseHandler(NullLogger<DialogueChooseHandler>.Instance, w.World)
                        .Execute(w.Connection, new CDialogueChoosePacket
                        {
                            TargetGuid = BankerWorld.BankerGuid.RawValue,
                            NodeId = BankerWorld.BankerRoot,
                            OptionId = BankerWorld.OpenBankOption,
                        });
                    Assert.NotNull(w.Connection.CurrentDialogue);
                    return w.Character.OpenBankNpc is null ? "none" : "bank opened";
                };
                break;
            case "bank move":
                w.OpenBank();
                w.Character.Container(InventoryType.Bag).Load([Item(0, EquipTemplates.Longsword)]);
                act = () =>
                {
                    new ItemMoveHandler(NullLogger<ItemMoveHandler>.Instance, w.World, economy)
                        .Execute(w.Connection, new CItemMovePacket
                        {
                            RequestId = ++request,
                            FromContainer = InventoryType.Bag,
                            FromSlot = 0,
                            ToContainer = InventoryType.Bank,
                            ToSlot = 0,
                        });
                    Assert.True(BankAccess.IsOpen(w.Connection, w.Character));
                    return w.Read<SItemResultPacket>(NetworkPacketType.SMSG_ITEM_RESULT)[^1].Result.ToString();
                };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }

        return (w.Character, act, null);
    }

    private static string ItemResult(ItemUseWorld w) =>
        w.Client.Read<SItemResultPacket>(NetworkPacketType.SMSG_ITEM_RESULT)[^1].Result.ToString();

    /// <summary>A plain clicked barrel beside the character, spent by one use.</summary>
    private static Doodad Barrel() => new(new ObjectGuid(ObjectType.Doodad, 770_801), new DoodadTemplateView(
            new DoodadTemplateId(1), "Barrel", "props-kit", "Barrel", null, null, null, null,
            DoodadInteraction.Click, 0, 5f, 0f, DoodadUseLimit.OncePerInstance, DoodadSpentLook.ShowUsed, null, null,
            null, 0, 0, 0, DoodadCollision.None, 0f, 0f, 0f, null, null, [], 0, ScriptCanSee: false),
        new Vector3(0f, 0f, 1f), 0f);
}
