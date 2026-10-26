using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.Parties;
using Avalon.Server.World.UnitTests.Seeding;
using Avalon.World;
using Avalon.World.Characters;
using Avalon.World.Auras;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Inventory;
using Avalon.World.Persistence;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Enums;
using Avalon.World.Testing.Scenarios;
using Microsoft.Extensions.Time.Testing;

namespace Avalon.Server.World.UnitTests.Combat;

/// <summary>
/// A worn sealed affix's bar (#1180), through a real instance over the seeded reference data: kills and the damage dealt
/// to creatures fill it, by the creature's rarity (Normal 1, Elite 3, Boss 20 points, the defaults) and only within five
/// levels of the wearer. A hit writes nothing; a save snapshot settles the whole points into the worn rows, each capped
/// at its item's threshold, and wakes nothing. The wearer is a level 10 warrior wearing an identified rare amulet
/// (template 69 on Neck) carrying "S5 Fire T9" sealed. No roll crits or evades. Character ids 1180_8xx, creature ids
/// 1180_98x.
/// </summary>
public class AwakeningShould
{
    private const ushort WearerLevel = 10;

    private static readonly int s_sealedAffix = CatalogKeys.ModifierId("S5 Fire T9");

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero));
    private readonly ScriptedCombatRandom _random = ScriptedCombatRandom.Plain();
    private readonly PartyTestWorld _parties = new();

    private static StaticData Data => ScenarioReferenceData.Shared.Data;

    /// <summary>
    /// Each row's progress once a save snapshot settled it. Every row but the one taken off settles nothing before the
    /// snapshot, so it also proves that its hits and kills marked nothing for a save and recorded nothing for the client.
    /// </summary>
    [Theory]
    [InlineData("a solo kill of a Normal creature in one hit", 2)]
    [InlineData("half an Elite's health and no kill", 1)]
    [InlineData("a kill of an Elite by a party member in range, the wearer dealing nothing", 3)]
    [InlineData("a kill of a Normal creature 6 levels below", 0)]
    [InlineData("a kill of a Normal creature exactly 5 levels below", 2)]
    [InlineData("the sealed amulet in the bag", 0)]
    [InlineData("a killing aura tick from the wearer", 2)]
    [InlineData("two sealed items worn, a solo Normal kill", 2)]
    [InlineData("taken off after half an Elite, then a Normal kill", 1)]
    [InlineData("a Boss of uint.MaxValue health, with no armour or resistance, one hit removing all of it", 40)]
    [InlineData("an item level 1 amulet at 45, a Boss kill", 50)]
    public void Fill_the_bar_from_kills_and_damage_while_worn(string @case, int expected)
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out _, world: MapInstanceClients.NewWorld(Data),
            random: _random, time: _clock, parties: _parties.Parties);
        CharacterEntity wearer = TestCharacters.New(1180_801, time: _clock);
        ICombatService combat = instance.CombatService;
        ItemInstanceId[] sealedItems;
        bool settledBefore = false;
        switch (@case)
        {
            case "a solo kill of a Normal creature in one hit":
            {
                sealedItems = Dress(instance, wearer);
                Creature foe = Foe(instance, 1180_981, CreatureRarity.Normal, health: 100);
                combat.ApplyDamage(wearer, foe, 1000);
                Assert.Equal(0u, foe.CurrentHealth);
                break;
            }

            case "half an Elite's health and no kill":
            {
                sealedItems = Dress(instance, wearer);
                Creature foe = Foe(instance, 1180_982, CreatureRarity.Elite, health: 200);
                combat.ApplyDamage(wearer, foe, 100);
                Assert.Equal(100u, foe.CurrentHealth);
                Assert.Equal(1500, TestCharacters.AwakeningOf(wearer).PendingMilli);
                break;
            }

            case "a kill of an Elite by a party member in range, the wearer dealing nothing":
            {
                PartyClient leader = _parties.Online(1180_802, level: WearerLevel, time: _clock);
                PartyClient member = _parties.Online(1180_803, level: WearerLevel, time: _clock);
                _parties.Form(leader, member);
                wearer = leader.Character;
                sealedItems = Dress(instance, wearer);
                MapInstanceClients.Join(instance, member.Character);
                Creature foe = Foe(instance, 1180_983, CreatureRarity.Elite, health: 100);
                combat.ApplyDamage(member.Character, foe, 1000);
                Assert.Equal(0u, foe.CurrentHealth);
                break;
            }

            case "a kill of a Normal creature 6 levels below":
            case "a kill of a Normal creature exactly 5 levels below":
            {
                sealedItems = Dress(instance, wearer);
                ushort level = (ushort)(WearerLevel - (@case.Contains('6', StringComparison.Ordinal) ? 6 : 5));
                Creature foe = Foe(instance, 1180_984, CreatureRarity.Normal, health: 100, level: level);
                combat.ApplyDamage(wearer, foe, 1000);
                Assert.Equal(0u, foe.CurrentHealth);
                break;
            }

            case "the sealed amulet in the bag":
            {
                sealedItems = Dress(instance, wearer, inBag: true);
                Creature foe = Foe(instance, 1180_985, CreatureRarity.Normal, health: 100);
                combat.ApplyDamage(wearer, foe, 1000);
                Assert.Equal(0u, foe.CurrentHealth);
                Assert.Equal((0, 0L), (TestCharacters.AwakeningOf(wearer).SealedWorn, TestCharacters.AwakeningOf(wearer).PendingMilli));
                break;
            }

            case "a killing aura tick from the wearer":
            {
                sealedItems = Dress(instance, wearer);
                Creature foe = Foe(instance, 1180_986, CreatureRarity.Normal, health: 10);
                ((CombatService)combat).ApplyPeriodicDamage(new PeriodicHit(wearer, foe, new AuraId(16), 100f,
                    new AuraSnapshot(100f, 0f, WearerLevel), AuraSource.None));
                Assert.Equal(0u, foe.CurrentHealth);
                break;
            }

            case "two sealed items worn, a solo Normal kill":
            {
                sealedItems = Dress(instance, wearer, twoItems: true);
                Assert.Equal(2, TestCharacters.AwakeningOf(wearer).SealedWorn);
                Creature foe = Foe(instance, 1180_987, CreatureRarity.Normal, health: 100);
                combat.ApplyDamage(wearer, foe, 1000);
                Assert.Equal(0u, foe.CurrentHealth);
                break;
            }

            case "taken off after half an Elite, then a Normal kill":
            {
                sealedItems = Dress(instance, wearer);
                Creature elite = Foe(instance, 1180_988, CreatureRarity.Elite, health: 200);
                combat.ApplyDamage(wearer, elite, 100);

                // The move settles first: the amulet keeps the 1 point it earned, and leaves the Equipment with it.
                CharacterInventoryService inventory = TestCharacters.InventoryFor(wearer, Data.FindItemTemplate);
                Assert.Equal(ItemRequestResult.Ok, inventory.TryMove(new SlotRef(InventoryType.Equipment, EquipmentSlots.Neck),
                    new SlotRef(InventoryType.Bag, 0), count: null, bankAccessible: false));
                Assert.True(CharacterStatsRefresh.Apply(wearer, Data, CurrentValues.KeepShare));
                Assert.Equal((0, 0L), (TestCharacters.AwakeningOf(wearer).SealedWorn, TestCharacters.AwakeningOf(wearer).PendingMilli));
                settledBefore = true;

                Creature normal = Foe(instance, 1180_989, CreatureRarity.Normal, health: 100);
                combat.ApplyDamage(wearer, normal, 1000);
                Assert.Equal(0u, normal.CurrentHealth);
                break;
            }

            case "a Boss of uint.MaxValue health, with no armour or resistance, one hit removing all of it":
            {
                // At item level 10 its threshold is 140, so the 40 are not capped.
                sealedItems = Dress(instance, wearer, itemLevel: 10);
                Creature boss = Foe(instance, 1180_990, CreatureRarity.Boss, health: uint.MaxValue);
                combat.ApplyDamage(wearer, boss, uint.MaxValue);
                Assert.Equal(0u, boss.CurrentHealth);
                Assert.Equal(40_000, TestCharacters.AwakeningOf(wearer).PendingMilli);
                break;
            }

            case "an item level 1 amulet at 45, a Boss kill":
            {
                sealedItems = Dress(instance, wearer, progress: 45);
                Creature boss = Foe(instance, 1180_991, CreatureRarity.Boss, health: 100);
                combat.ApplyDamage(wearer, boss, 1000);
                Assert.Equal(0u, boss.CurrentHealth);
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(@case), @case, null);
        }

        if (!settledBefore)
        {
            foreach (ItemInstanceId item in sealedItems)
                Assert.Equal(SaveState.Unchanged, wearer.SaveState.ItemState(item));
            Assert.False(wearer.ClientChanges.HasChanges);
        }

        CharacterSaveSnapshot.Take(wearer);

        // Each sealed row reads the same, and is still sealed: a settle never wakes one.
        Assert.All(sealedItems, item => Assert.Equal(expected, SealedRowOf(wearer, item).SealProgress));
        if (@case.StartsWith("half an Elite", StringComparison.Ordinal))
            Assert.Equal(500, TestCharacters.AwakeningOf(wearer).PendingMilli);
    }

    /// <summary>
    /// <paramref name="character" />, at level 10, wearing (or holding in its bag) an identified rare amulet of
    /// <paramref name="itemLevel" /> with "S5 Fire T9" sealed at <paramref name="progress" />, and a second such piece
    /// (a ring on Finger1) when <paramref name="twoItems" /> says; its stats refreshed as it enters the world, which counts
    /// the worn seals, and joined to the instance. Answers the sealed items' ids.
    /// </summary>
    private static ItemInstanceId[] Dress(MapInstance instance, CharacterEntity character, ushort itemLevel = 1,
        int progress = 0, bool inBag = false, bool twoItems = false)
    {
        character.Level = WearerLevel;
        ItemModifierStat line = ItemModifierSeed.Stats().Where(l => l.ModifierId == s_sealedAffix).OrderBy(l => l.Index).First();
        (byte Slot, ulong Template)[] kit = twoItems
            ? [(EquipmentSlots.Neck, 69), (EquipmentSlots.Finger1, 68)]
            : [(inBag ? (byte)0 : EquipmentSlots.Neck, 69)];
        InventoryItem[] pieces =
        [
            .. kit.Select(piece => TestCharacters.Item(piece.Slot, Data.FindItemTemplate(new ItemTemplateId(piece.Template))!)
                with { Rarity = ItemRarity.Rare, ItemLevel = itemLevel }),
        ];
        character.ItemModifiers.Load(
        [
            .. pieces.Select(piece =>
            {
                ItemInstanceModifier sealedRow =
                    TestModifiers.Modifier(piece.InstanceId, s_sealedAffix, 0, (0, line.Stat, line.Min, line.Min2));
                sealedRow.SealProgress = progress;
                return new ItemInstance
                {
                    Identified = true, Id = piece.InstanceId, TemplateId = piece.TemplateId, Modifiers = [sealedRow],
                };
            }),
        ]);
        character.Container(inBag ? InventoryType.Bag : InventoryType.Equipment).Load(pieces);
        Assert.True(CharacterStatsRefresh.Apply(character, Data, CurrentValues.Refill));
        MapInstanceClients.Join(instance, character);
        return [.. pieces.Select(piece => piece.InstanceId)];
    }

    /// <summary>A creature of <paramref name="rarity" /> beside the origin, with no armour or resistance.</summary>
    private static Creature Foe(MapInstance instance, uint id, CreatureRarity rarity, uint health, ushort level = WearerLevel)
    {
        CreatureTemplate template = Loot.LootTestData.BoarTemplate(null);
        template.Rarity = rarity;
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            Metadata = template,
            Position = new Vector3(0f, 0f, 2f),
            Level = level,
            Health = health,
            CurrentHealth = health,
            ControlResistance = ControlResistance.Of(template, Data.CreatureRarityModifiers),
        };
        creature.Script = new CombatResolutionShould.CountingWoundScript(creature);
        instance.AddCreature(creature);
        return creature;
    }

    private static ItemInstanceModifier SealedRowOf(CharacterEntity character, ItemInstanceId item) =>
        Assert.Single(character.ItemModifiers.Of(item), row => row.ModifierId == s_sealedAffix);
}
