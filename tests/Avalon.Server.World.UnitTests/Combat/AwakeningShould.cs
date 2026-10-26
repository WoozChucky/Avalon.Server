using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.Server.World.UnitTests.Parties;
using Avalon.Server.World.UnitTests.Seeding;
using Avalon.Server.World.UnitTests.World;
using Avalon.World;
using Avalon.World.Auras;
using Avalon.World.Characters;
using Avalon.World.Combat;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Inventory;
using Avalon.World.Items;
using Avalon.World.Loot;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Scripts.Abstractions;
using Avalon.World.Testing.Scenarios;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

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
    /// A row ending "then the tick's flush" is read through the tick's Awakening flush instead: a full bar wakes, sent once
    /// and with no progress for the row that woke; a partial one is sent once as its slot's progress.
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
    [InlineData("an item level 1 amulet at 45, a Boss kill, then the tick's flush", 50)]
    [InlineData("a solo kill of a Normal creature in one hit, then the tick's flush", 2)]
    [InlineData("an Elite with no script, which ignores the hit", 0)]
    [InlineData("an unidentified copy worn, a solo Normal kill", 0)]
    [InlineData("a tainted sealed amulet worn, a solo Normal kill", 17)]
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
            case "a solo kill of a Normal creature in one hit, then the tick's flush":
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
            case "an item level 1 amulet at 45, a Boss kill, then the tick's flush":
            {
                sealedItems = Dress(instance, wearer, progress: 45);
                Creature boss = Foe(instance, 1180_991, CreatureRarity.Boss, health: 100);
                combat.ApplyDamage(wearer, boss, 1000);
                Assert.Equal(0u, boss.CurrentHealth);
                break;
            }

            case "an Elite with no script, which ignores the hit":
            {
                // The meter reads the health the creature lost, not what it was handed.
                sealedItems = Dress(instance, wearer);
                Creature foe = Foe(instance, 1180_992, CreatureRarity.Elite, health: 200, scripted: false);
                combat.ApplyDamage(wearer, foe, 100);
                Assert.Equal((200u, 0L), (foe.CurrentHealth, TestCharacters.AwakeningOf(wearer).PendingMilli));
                break;
            }

            case "an unidentified copy worn, a solo Normal kill":
            {
                // Should one ever be worn, it neither fills nor tells its owner it is sealed: never counted, never queued.
                sealedItems = Dress(instance, wearer, identified: false);
                Creature foe = Foe(instance, 1180_993, CreatureRarity.Normal, health: 100);
                combat.ApplyDamage(wearer, foe, 1000);
                Assert.Equal(0u, foe.CurrentHealth);
                Assert.Equal((0, 0L), (TestCharacters.AwakeningOf(wearer).SealedWorn, TestCharacters.AwakeningOf(wearer).PendingMilli));
                var sent = new List<OutboundPacket>();
                AwakeningStage stage = Stage(new ScriptedLootRandom([]));
                stage.Queue.Attach(wearer, Recording(wearer, sent));
                stage.Tick();
                Assert.Empty(sent);
                break;
            }

            case "a tainted sealed amulet worn, a solo Normal kill":
            {
                // A Tainted copy (#1180 part C) keeps its bar where it stood, frozen: never counted, never queued.
                sealedItems = Dress(instance, wearer, progress: 17, tainted: true);
                Creature foe = Foe(instance, 1180_994, CreatureRarity.Normal, health: 100);
                combat.ApplyDamage(wearer, foe, 1000);
                Assert.Equal(0u, foe.CurrentHealth);
                Assert.Equal((0, 0L), (TestCharacters.AwakeningOf(wearer).SealedWorn, TestCharacters.AwakeningOf(wearer).PendingMilli));
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

        if (@case.EndsWith(", then the tick's flush", StringComparison.Ordinal))
        {
            AssertFlushed(wearer, sealedItems[0], expected);
            return;
        }

        CharacterSaveSnapshot.Take(wearer);

        // Each sealed row reads the same, and is still sealed: a settle never wakes one.
        Assert.All(sealedItems, item => Assert.Equal(expected, SealedRowOf(wearer, item).SealProgress));
        if (@case.StartsWith("half an Elite", StringComparison.Ordinal))
            Assert.Equal(500, TestCharacters.AwakeningOf(wearer).PendingMilli);
    }

    /// <summary>
    /// The wake (#1180), through the tick's Awakening flush over the seeded catalog: an identified rare amulet worn on Neck
    /// carrying an awake "S1 T9" (Thorn) and a sealed "S5 Fire" row of the case's tier, item level and progress. A full
    /// bar wakes: as it was on a missed draw (the default 20 %), one tier better when that tier's item level is within the
    /// item's plus 3, and otherwise polished, each value rolled again within its tier and the higher kept. A Tainted copy's
    /// full bar never wakes (#1180 part C). <paramref name="expected" /> is the row as "{key} {outcome} {values}", or "sealed" when nothing woke.
    /// </summary>
    [Theory]
    [InlineData("T9 at item level 1 at 50, the draw 0.2 misses", "S5 Fire T9 Woken 1")]
    [InlineData("T9 at item level 1 at 50, the draw 0.19 hits", "S5 Fire T8 TierRaised 3")]
    [InlineData("T8 at item level 1 at 50, a hit: T7 out of reach", "S5 Fire T8 Polished 3")]
    [InlineData("T8 at item level 4 at 80, a hit", "S5 Fire T7 TierRaised 4")]
    [InlineData("T7 at item level 10 at 140, a hit, the new roll lower", "S5 Fire T7 Polished 4")]
    [InlineData("T7 at item level 10 at 140, a hit, the new roll higher", "S5 Fire T7 Polished 4")]
    [InlineData("one point short", "sealed")]
    [InlineData("in the bag at its threshold", "sealed")]
    [InlineData("already attuned, at its threshold, a miss", "S5 Fire T9 Woken 1")]
    [InlineData("tainted, at its threshold", "sealed")]
    public void Wake_at_the_threshold_through_the_tick_flush(string @case, string expected)
    {
        // No roll makes a T8 seal at item level 1 today (T8 needs 4); the row holds the reach rule for later tiers.
        WakeCase setup = @case switch
        {
            "T9 at item level 1 at 50, the draw 0.2 misses" => new("T9", 1, 50, 1, [0.2], []),
            "T9 at item level 1 at 50, the draw 0.19 hits" => new("T9", 1, 50, 1, [0.19], [3]),
            "T8 at item level 1 at 50, a hit: T7 out of reach" => new("T8", 1, 50, 2, [0.19], [3]),
            "T8 at item level 4 at 80, a hit" => new("T8", 4, 80, 2, [0.19], [4]),
            "T7 at item level 10 at 140, a hit, the new roll lower" => new("T7", 10, 140, 4, [0.19], [3]),
            "T7 at item level 10 at 140, a hit, the new roll higher" => new("T7", 10, 140, 3, [0.19], [4]),
            "one point short" => new("T9", 1, 49, 1, [], []),
            "in the bag at its threshold" => new("T9", 1, 50, 1, [], [], InBag: true),
            "already attuned, at its threshold, a miss" => new("T9", 1, 50, 1, [0.5], [], Attuned: true),
            "tainted, at its threshold" => new("T9", 1, 50, 1, [], [], Tainted: true),
            _ => throw new ArgumentOutOfRangeException(nameof(@case), @case, null),
        };

        CharacterEntity character = TestCharacters.New(1180_811, time: _clock);
        character.Level = WearerLevel;
        InventoryType container = setup.InBag ? InventoryType.Bag : InventoryType.Equipment;
        byte slot = setup.InBag ? (byte)0 : EquipmentSlots.Neck;
        InventoryItem amulet = TestCharacters.Item(slot, Data.FindItemTemplate(new ItemTemplateId(69))!) with
        {
            Rarity = ItemRarity.Rare,
            ItemLevel = setup.ItemLevel,
            Flags = setup.Attuned ? ItemInstanceFlags.Attuned : ItemInstanceFlags.None,
            Tainted = setup.Tainted,
        };
        ItemInstanceModifier awake = TestModifiers.Modifier(amulet.InstanceId, CatalogKeys.ModifierId("S1 T9"), 0,
            (0, StatType.Strength, 1, null));
        ItemInstanceModifier sealedRow = TestModifiers.Modifier(amulet.InstanceId, CatalogKeys.ModifierId($"S5 Fire {setup.Tier}"),
            1, (0, StatType.FireResistance, setup.Value, null));
        sealedRow.SealProgress = setup.Progress;
        character.ItemModifiers.Load(
            [new ItemInstance { Identified = true, Id = amulet.InstanceId, TemplateId = amulet.TemplateId, Modifiers = [awake, sealedRow] }]);
        character.Container(container).Load([amulet]);
        Assert.True(CharacterStatsRefresh.Apply(character, Data, CurrentValues.EnterWorld));

        IReadOnlyList<ItemInstanceModifier> before = character.ItemModifiers.Of(amulet.InstanceId);
        float fireBefore = character.Stats!.Value.Resistances.Fire;
        if (!setup.InBag)
            Assert.Equal("Thorn 1", ResonanceNamed(character));

        // Attached after the refresh, as the world attaches a character entering an instance after its select: its meter,
        // due since the refresh counted its seal, queues it then.
        var draws = new ScriptedLootRandom(setup.Doubles, setup.Integers);
        AwakeningStage stage = Stage(draws);
        var sent = new List<OutboundPacket>();
        stage.Queue.Attach(character, Recording(character, sent));
        stage.Tick();

        ItemInstanceModifier row = character.ItemModifiers.Of(amulet.InstanceId)[1];
        if (expected == "sealed")
        {
            Assert.Equal(setup.Progress, row.SealProgress);
            Assert.Same(before, character.ItemModifiers.Of(amulet.InstanceId));
            Assert.Empty(sent);
            Assert.Equal(SaveState.Unchanged, character.SaveState.ItemState(amulet.InstanceId));
            Assert.False(character.ClientChanges.HasChanges);
            Assert.True(draws.Exhausted);
            return;
        }

        SItemAwakenedPacket awakened = Assert.Single(TestTown.Read<SItemAwakenedPacket>(sent, NetworkPacketType.SMSG_ITEM_AWAKENED));
        Assert.Equal(expected,
            $"{KeyOf(row.ModifierId)} {awakened.Outcome} {string.Join(",", row.Values.Select(v => v.Value))}");
        Assert.Empty(TestTown.Read<SAwakeningProgressPacket>(sent, NetworkPacketType.SMSG_AWAKENING_PROGRESS));
        Assert.Equal(((uint)EquipmentSlots.Neck, (uint)row.ModifierId), (awakened.EquipmentSlot, awakened.ModifierId));

        // A new list and a new row in the sealed one's place, keeping its id and ordinal, no longer sealed.
        Assert.NotSame(before, character.ItemModifiers.Of(amulet.InstanceId));
        Assert.Equal((sealedRow.Id, sealedRow.Ordinal, (int?)null), (row.Id, row.Ordinal, row.SealProgress));
        Assert.True((TestCharacters.At(character, InventoryType.Equipment, EquipmentSlots.Neck).Flags & ItemInstanceFlags.Attuned) != 0);
        Assert.Contains((InventoryType.Equipment, EquipmentSlots.Neck), character.ClientChanges.Slots);

        // The stats were refreshed and Resonance counted again: the woken value adds its fire resistance, and Ember.
        Assert.Equal((0f, (float)row.Values[0].Value), (fireBefore, character.Stats!.Value.Resistances.Fire));
        Assert.Equal("Ember 1, Thorn 1", ResonanceNamed(character));

        // Sent once: the next tick's flush has nothing to say.
        sent.Clear();
        stage.Tick();
        Assert.Empty(sent);
        Assert.True(draws.Exhausted);
        Assert.True(character.SaveState.TakeMarks().Items[amulet.InstanceId].Modifiers);
    }

    /// <summary>
    /// The production wiring (#1180): the world's spawn attaches a wearer to its Awakening queue, so the tick's stage
    /// visits it and sends through its connection; leaving the world detaches it, so its meter queues it no more. The
    /// wearer is due from the refresh that counted its seal (an amulet at 10 of item level 1's 50), and a kill's point
    /// settles to 11.
    /// </summary>
    [Fact]
    public async Task Queue_a_wearer_for_the_tick_only_while_the_world_holds_it()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out _, world: MapInstanceClients.NewWorld(Data),
            random: _random, time: _clock, parties: _parties.Parties);
        CharacterEntity wearer = TestCharacters.New(1180_821, time: _clock);
        ItemInstanceId amulet = Dress(instance, wearer, progress: 10)[0];
        var queue = new AwakeningQueue();
        Avalon.World.World world = await ScriptHotReloadPollingShould.BuildWorldAsync(
            Substitute.For<IScriptHotReloader>(), intervalSeconds: 60, awakening: queue);
        AwakeningStage stage = Stage(new ScriptedLootRandom([])) with { Queue = queue };
        var sent = new List<OutboundPacket>();
        IWorldConnection connection = Recording(wearer, sent);

        world.SpawnInInstance(connection, Substitute.For<IMapInstance>());
        Assert.Equal([wearer], queue.Take().ToArray());
        stage.Flusher.Flush(wearer, Data);
        Assert.Empty(sent);

        // In the world: a kill queues it, and the tick sends the settled bar through its connection.
        TestCharacters.AwakeningOf(wearer).AddKill(1);
        stage.Tick();
        Assert.Equal([((uint)EquipmentSlots.Neck, amulet.Value, 11u)],
            Assert.Single(TestTown.Read<SAwakeningProgressPacket>(sent, NetworkPacketType.SMSG_AWAKENING_PROGRESS))
                .Items.Select(entry => (entry.EquipmentSlot, entry.ItemInstanceId, entry.Progress)));

        // Gone: a kill credited after the leave queues nothing.
        await world.LeaveWorldAsync(connection);
        TestCharacters.AwakeningOf(wearer).AddKill(1);
        Assert.True(queue.Take().IsEmpty);
    }

    /// <summary>
    /// The tick's Awakening flush for <paramref name="wearer" />, whose amulet on Neck settled to
    /// <paramref name="progress" />, run for two ticks once the wearer is attached: a full bar (item level 1's 50) wakes,
    /// sent once with no progress for it; any other bar is sent once as Neck's progress, naming the amulet
    /// <paramref name="amulet" />. A wake misses its tier-up draw.
    /// </summary>
    private static void AssertFlushed(CharacterEntity wearer, ItemInstanceId amulet, int progress)
    {
        bool full = progress == ItemAwakening.Threshold(1);
        var draws = new ScriptedLootRandom(full ? [0.5] : []);
        AwakeningStage stage = Stage(draws);
        var sent = new List<OutboundPacket>();
        stage.Queue.Attach(wearer, Recording(wearer, sent));
        stage.Tick();
        stage.Tick();

        List<SItemAwakenedPacket> woken = TestTown.Read<SItemAwakenedPacket>(sent, NetworkPacketType.SMSG_ITEM_AWAKENED);
        List<SAwakeningProgressPacket> bars = TestTown.Read<SAwakeningProgressPacket>(sent, NetworkPacketType.SMSG_AWAKENING_PROGRESS);
        if (full)
        {
            Assert.Equal((uint)EquipmentSlots.Neck, Assert.Single(woken).EquipmentSlot);
            Assert.Empty(bars);
        }
        else
        {
            Assert.Empty(woken);
            Assert.Equal([((uint)EquipmentSlots.Neck, amulet.Value, (uint)progress)],
                Assert.Single(bars).Items.Select(entry => (entry.EquipmentSlot, entry.ItemInstanceId, entry.Progress)));
        }

        Assert.True(draws.Exhausted);
    }

    /// <summary>
    /// The tick's Awakening stage, as WorldServer runs it: the queue the world attaches characters to, and the flush over
    /// an economy on the seeded data, its roller drawing from <paramref name="draws" />.
    /// </summary>
    private static AwakeningStage Stage(ILootRandom draws)
    {
        var ids = new ItemIdAllocator();
        var economy = new CharacterEconomy(MapInstanceClients.NewWorld(Data), ids,
            new ItemRoller(draws, ids, Options.Create(new GameConfiguration())));
        return new AwakeningStage(new AwakeningFlusher(economy, NullLogger.Instance), new AwakeningQueue());
    }

    /// <summary>The stage's flush and its queue; a tick flushes each character the queue took.</summary>
    private sealed record AwakeningStage(AwakeningFlusher Flusher, AwakeningQueue Queue)
    {
        public void Tick()
        {
            foreach (CharacterEntity character in Queue.Take())
                Flusher.Flush(character, Data);
        }
    }

    private static IWorldConnection Recording(CharacterEntity character, List<OutboundPacket> sent)
    {
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        TestTown.Record(connection, character, sent);
        return connection;
    }

    private static string KeyOf(int modifierId) => CatalogKeys.All().Single(row => row.ModifierId == modifierId).Key;

    /// <summary>The character's Resonance counts above 0 as "Theme count", by the seeded theme names.</summary>
    private static string ResonanceNamed(CharacterEntity character)
    {
        Dictionary<int, string> names = ItemModifierSeed.Themes().ToDictionary(theme => theme.Id, theme => theme.Name);
        List<string> counted = [];
        foreach (ThemeCount pair in TestCharacters.ResonanceOf(character))
            counted.Add($"{names[pair.ThemeId]} {pair.Count}");
        return string.Join(", ", counted);
    }

    private sealed record WakeCase(string Tier, ushort ItemLevel, int Progress, int Value, double[] Doubles, long[] Integers,
        bool InBag = false, bool Attuned = false, bool Tainted = false);

    /// <summary>
    /// <paramref name="character" />, at level 10, wearing (or holding in its bag) an identified rare amulet of
    /// <paramref name="itemLevel" /> with "S5 Fire T9" sealed at <paramref name="progress" />, and a second such piece
    /// (a ring on Finger1) when <paramref name="twoItems" /> says; its stats refreshed as it enters the world, which counts
    /// the worn seals, and joined to the instance. Answers the sealed items' ids. Unidentified when
    /// <paramref name="identified" /> says so, which the equip rule never lets a character wear; Tainted when
    /// <paramref name="tainted" /> says so.
    /// </summary>
    private static ItemInstanceId[] Dress(MapInstance instance, CharacterEntity character, ushort itemLevel = 1,
        int progress = 0, bool inBag = false, bool twoItems = false, bool identified = true, bool tainted = false)
    {
        character.Level = WearerLevel;
        ItemModifierStat line = ItemModifierSeed.Stats().Where(l => l.ModifierId == s_sealedAffix).OrderBy(l => l.Index).First();
        (byte Slot, ulong Template)[] kit = twoItems
            ? [(EquipmentSlots.Neck, 69), (EquipmentSlots.Finger1, 68)]
            : [(inBag ? (byte)0 : EquipmentSlots.Neck, 69)];
        InventoryItem[] pieces =
        [
            .. kit.Select(piece => TestCharacters.Item(piece.Slot, Data.FindItemTemplate(new ItemTemplateId(piece.Template))!,
                identified: identified) with { Rarity = ItemRarity.Rare, ItemLevel = itemLevel, Tainted = tainted }),
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
                    Identified = identified, Id = piece.InstanceId, TemplateId = piece.TemplateId, Modifiers = [sealedRow],
                };
            }),
        ]);
        character.Container(inBag ? InventoryType.Bag : InventoryType.Equipment).Load(pieces);
        Assert.True(CharacterStatsRefresh.Apply(character, Data, CurrentValues.Refill));
        MapInstanceClients.Join(instance, character);
        return [.. pieces.Select(piece => piece.InstanceId)];
    }

    /// <summary>
    /// A creature of <paramref name="rarity" /> beside the origin, with no armour or resistance, whose script takes each
    /// hit off its health; with none when <paramref name="scripted" /> says so, so a hit takes nothing off.
    /// </summary>
    private static Creature Foe(MapInstance instance, uint id, CreatureRarity rarity, uint health, ushort level = WearerLevel,
        bool scripted = true)
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
        if (scripted)
            creature.Script = new CombatResolutionShould.CountingWoundScript(creature);
        instance.AddCreature(creature);
        return creature;
    }

    private static ItemInstanceModifier SealedRowOf(CharacterEntity character, ItemInstanceId item) =>
        Assert.Single(character.ItemModifiers.Of(item), row => row.ModifierId == s_sealedAffix);
}
