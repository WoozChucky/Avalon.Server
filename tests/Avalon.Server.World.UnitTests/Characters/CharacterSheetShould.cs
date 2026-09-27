using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Reload;
using Avalon.World;
using Avalon.World.Characters;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Inventory;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Reload;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Characters;

/// <summary>
/// #506 slice C: the character sheet reaches its owner, and only its owner, when the character enters the
/// world and whenever a value it shows changes, at most once per tick, with its chances clamped to the
/// current combat formula's caps.
/// </summary>
public class CharacterSheetShould
{
    private static readonly CombatFormula Seeded = CombatSeed.Formula();

    private static readonly ClassLevelStat[] WarriorRows =
    [
        new() { Class = CharacterClass.Warrior, Level = 1, BaseHp = 20, BaseMana = 0, Stamina = 22, Strength = 23, Agility = 20, Intellect = 20 },
        new() { Class = CharacterClass.Warrior, Level = 2, BaseHp = 40, BaseMana = 0, Stamina = 24, Strength = 25, Agility = 21, Intellect = 20 },
    ];

    private static IWorldConnection Recording(CharacterEntity character, List<NetworkPacket> sent)
    {
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        TestTown.Record(connection, character, sent);
        return connection;
    }

    private static List<SCharacterStatsPacket> Sheets(List<NetworkPacket> sent) =>
        TestTown.Read<SCharacterStatsPacket>(sent, NetworkPacketType.SMSG_CHARACTER_STATS);

    private static DerivedCharacterStats Stats(float crit = 5f, float dodge = 4f, float block = 3f, uint strength = 23) =>
        new(MaxHealth: 240, MaxPower: 100, Stamina: 22, Strength: strength, Agility: 20, Intellect: 21, Armor: 8,
            BlockPct: block, DodgePct: dodge, CritPct: crit, AttackDamage: 46, AbilityDamage: 12, WeaponMin: 3, WeaponMax: 7);

    [Fact]
    public void Send_one_sheet_with_the_characters_values_once_the_selected_character_is_in_the_world()
    {
        CharacterEntity character = New();
        character.ApplyStats(Stats(), CurrentValues.EnterWorld);
        var sent = new List<NetworkPacket>();
        IWorldConnection connection = PendingSpawnConnection.Create(
            new PendingSpawn(character, Substitute.For<IMapInstance>(), DateTime.UtcNow.Ticks));
        connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        connection.When(c => c.Send(Arg.Any<NetworkPacket>())).Do(ci => sent.Add(ci.Arg<NetworkPacket>()));

        CharacterSheetFlusher.Flush(connection, Seeded);
        Assert.Empty(sent);

        Assert.True(CharacterReadinessBarrier.Release(connection, Substitute.For<IWorld>(), NullLogger.Instance));
        CharacterSheetFlusher.Flush(connection, Seeded);
        CharacterSheetFlusher.Flush(connection, Seeded);

        SCharacterStatsPacket sheet = Assert.Single(Sheets(sent));
        Assert.Equal((22u, 23u, 20u, 21u, 8u, 46u, 12u),
            (sheet.Stamina, sheet.Strength, sheet.Agility, sheet.Intellect, sheet.Armor, sheet.AttackDamage, sheet.AbilityDamage));
        Assert.Equal((5f, 4f, 3f, 3u, 7u), (sheet.CritPct, sheet.DodgePct, sheet.BlockPct, sheet.WeaponMin, sheet.WeaponMax));
    }

    [Fact]
    public void Send_nothing_for_a_character_with_no_stats_yet()
    {
        var sent = new List<NetworkPacket>();

        CharacterSheetFlusher.Flush(Recording(New(), sent), Seeded);

        Assert.Empty(sent);
    }

    [Fact]
    public async Task Send_a_sheet_after_an_equip_that_changes_a_stat_and_none_after_one_that_changes_nothing()
    {
        BankerWorld w = await BankerWorld.CreateAsync();
        var move = new ItemMoveHandler(NullLogger<ItemMoveHandler>.Instance, w.World, new CharacterEconomy(w.World, new ItemIdAllocator()));
        w.Character.Container(InventoryType.Bag).Load([Item(0, EquipTemplates.Chestguard), Item(1, EquipTemplates.Band)]);
        CharacterSheetFlusher.Flush(w.Connection, w.Data.Combat.Formula);
        uint armourBefore = Assert.Single(w.Read<SCharacterStatsPacket>(NetworkPacketType.SMSG_CHARACTER_STATS)).Armor;
        w.Sent.Clear();

        move.Execute(w.Connection, new CItemMovePacket
        {
            RequestId = 1, FromContainer = (uint)InventoryType.Bag, FromSlot = 1,
            ToContainer = (uint)InventoryType.Equipment, ToSlot = EquipmentSlots.Finger1,
        });
        CharacterSheetFlusher.Flush(w.Connection, w.Data.Combat.Formula);
        Assert.Empty(w.Read<SCharacterStatsPacket>(NetworkPacketType.SMSG_CHARACTER_STATS));

        move.Execute(w.Connection, new CItemMovePacket
        {
            RequestId = 2, FromContainer = (uint)InventoryType.Bag, FromSlot = 0,
            ToContainer = (uint)InventoryType.Equipment, ToSlot = EquipmentSlots.Chest,
        });
        CharacterSheetFlusher.Flush(w.Connection, w.Data.Combat.Formula);

        SCharacterStatsPacket sheet = Assert.Single(w.Read<SCharacterStatsPacket>(NetworkPacketType.SMSG_CHARACTER_STATS));
        Assert.Equal(armourBefore + 8u, sheet.Armor);
        Assert.Equal(25u, sheet.Strength);
    }

    [Fact]
    public async Task Send_a_sheet_after_a_level_up()
    {
        StaticData data = await TestStaticData.LoadAsync(
            classStats: WarriorRows,
            levels:
            [
                new CharacterLevelExperience { Level = 1, Experience = 100 },
                new CharacterLevelExperience { Level = 2, Experience = 500 },
            ]);
        IWorld world = Substitute.For<IWorld>();
        world.Configuration.Returns(new GameConfiguration());
        world.MapTemplates.Returns(new List<MapTemplate> { new() { Id = new MapTemplateId(1), Name = "Town" } });
        world.Data.Returns(data);
        using MapInstance instance = TestMapInstances.Build(world);
        CharacterEntity killer = New();
        CharacterStatsRefresh.Apply(killer, data, CurrentValues.EnterWorld);
        var sent = new List<NetworkPacket>();
        IWorldConnection connection = Recording(killer, sent);
        CharacterSheetFlusher.Flush(connection, data.Combat.Formula);
        Assert.Equal(23u, Assert.Single(Sheets(sent)).Strength);
        sent.Clear();

        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 506_901),
            Metadata = Substitute.For<ICreatureMetadata>(),
            Experience = 150,
        };
        instance.AddCreature(creature);
        instance.ReportKill(creature, killer);
        CharacterSheetFlusher.Flush(connection, data.Combat.Formula);

        SCharacterStatsPacket sheet = Assert.Single(Sheets(sent));
        Assert.Equal((24u, 25u, 21u), (sheet.Stamina, sheet.Strength, sheet.Agility));
    }

    [Fact]
    public void Send_each_chance_clamped_to_its_cap()
    {
        CharacterEntity character = New();
        character.ApplyStats(Stats(crit: 80f, dodge: 45f, block: 70f), CurrentValues.EnterWorld);
        var sent = new List<NetworkPacket>();

        CharacterSheetFlusher.Flush(Recording(character, sent), Seeded);

        SCharacterStatsPacket sheet = Assert.Single(Sheets(sent));
        Assert.Equal((Seeded.CritCap, Seeded.DodgeCap, Seeded.BlockCap), (sheet.CritPct, sheet.DodgePct, sheet.BlockPct));
        Assert.Equal((50f, 30f, 50f), (sheet.CritPct, sheet.DodgePct, sheet.BlockPct));
    }

    [Fact]
    public async Task Send_a_sheet_after_a_combat_reload_only_to_the_characters_whose_values_it_changed()
    {
        var rows = new CombatReloadShould.Rows();
        StaticData data = await TestStaticData.LoadAsync(
            TestStaticData.Repositories(classStats: () => WarriorRows, combat: rows.Repository()));
        CharacterEntity keen = New(1);
        keen.ApplyStats(Stats(crit: 30f), CurrentValues.EnterWorld);
        CharacterEntity dull = New(2);
        dull.ApplyStats(Stats(crit: 5f), CurrentValues.EnterWorld);
        var keenSent = new List<NetworkPacket>();
        var dullSent = new List<NetworkPacket>();
        IWorldConnection keenConnection = Recording(keen, keenSent);
        IWorldConnection dullConnection = Recording(dull, dullSent);
        CharacterSheetFlusher.Flush(keenConnection, data.Combat.Formula);
        CharacterSheetFlusher.Flush(dullConnection, data.Combat.Formula);
        keenSent.Clear();
        dullSent.Clear();

        rows.Formulas.Single().CritCap = 20f;
        Task applied = data.ApplyOnNextTickAsync(await data.PrepareAsync(ReloadArea.Combat));
        data.ApplyPending();
        await applied;
        CharacterSheetFlusher.Flush(keenConnection, data.Combat.Formula);
        CharacterSheetFlusher.Flush(dullConnection, data.Combat.Formula);

        Assert.Equal(20f, Assert.Single(Sheets(keenSent)).CritPct);
        Assert.Empty(dullSent);
    }

    [Fact]
    public void Never_send_a_characters_sheet_to_another_player()
    {
        CharacterEntity owner = New(1);
        owner.ApplyStats(Stats(), CurrentValues.EnterWorld);
        CharacterEntity other = New(2);
        other.ApplyStats(Stats(), CurrentValues.EnterWorld);
        var ownerSent = new List<NetworkPacket>();
        var otherSent = new List<NetworkPacket>();
        IWorldConnection ownerConnection = Recording(owner, ownerSent);
        IWorldConnection otherConnection = Recording(other, otherSent);
        CharacterSheetFlusher.Flush(ownerConnection, Seeded);
        CharacterSheetFlusher.Flush(otherConnection, Seeded);
        ownerSent.Clear();
        otherSent.Clear();

        owner.ApplyStats(Stats(strength: 40), CurrentValues.KeepShare);
        CharacterSheetFlusher.Flush(ownerConnection, Seeded);
        CharacterSheetFlusher.Flush(otherConnection, Seeded);

        Assert.Equal(40u, Assert.Single(Sheets(ownerSent)).Strength);
        Assert.Empty(otherSent);
    }

    [Fact]
    public void Send_at_most_one_sheet_per_tick_with_the_last_values()
    {
        CharacterEntity character = New();
        character.ApplyStats(Stats(), CurrentValues.EnterWorld);
        var sent = new List<NetworkPacket>();
        IWorldConnection connection = Recording(character, sent);
        CharacterSheetFlusher.Flush(connection, Seeded);
        sent.Clear();

        // A gear change and a level-up in one tick: the flush at its end sends one sheet.
        character.ApplyStats(Stats(strength: 30), CurrentValues.KeepShare);
        character.ApplyStats(Stats(strength: 35), CurrentValues.Refill);
        CharacterSheetFlusher.Flush(connection, Seeded);

        Assert.Equal(35u, Assert.Single(Sheets(sent)).Strength);
    }
}
