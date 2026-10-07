using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Quest;
using Avalon.Network.Packets.Social;
using Avalon.Network.Packets.State;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Items;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Respawn;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.ItemUse;

/// <summary>The toolbox over one use: every member through the service that owns the change.</summary>
public class ItemUseContextShould
{
    private readonly CharacterEntity _character = New(7, money: 50);
    private readonly IWorldConnection _connection = Substitute.For<IWorldConnection>();
    private readonly IItemUseHost _host = Substitute.For<IItemUseHost>();
    private readonly IRespawnTargetResolver _resolver = Substitute.For<IRespawnTargetResolver>();
    private readonly ICreaturePlacementService _placement = Substitute.For<ICreaturePlacementService>();
    private readonly IWorld _world = Substitute.For<IWorld>();
    private readonly List<NetworkPacket> _sent = [];
    private readonly InventoryItem _stack;

    public ItemUseContextShould()
    {
        _stack = Item(0, Potion, count: 3);
        _character.Container(InventoryType.Bag).Load([_stack]);
        TestTown.Record(_connection, _character, _sent);
        _world.Configuration.Returns(new GameConfiguration());
        _world.MapTemplates.Returns(new List<MapTemplate>());
        _host.MapType.Returns(MapType.Normal);
        _resolver.ResolveTownAsync(default!, default).ReturnsForAnyArgs(Task.FromResult(new MapTemplateId(1)));
    }

    private ItemUseContext Context(ItemTemplate? template = null) => new(
        _connection, _character, _host, _stack, template ?? Potion,
        new ItemUseTools(_world, new CharacterEconomy(_world, new ItemIdAllocator()),
            new TownReturn(NullLogger.Instance, _world, _resolver, Substitute.For<IChunkLibrary>()),
            new MapTeleport(NullLogger<MapTeleport>.Instance, _world, Substitute.For<IChunkLibrary>(), Substitute.For<Avalon.World.Persistence.ICharacterSaver>()),
            _placement, new FakeTimeProvider(), NullLogger<ItemUseTools>.Instance));

    [Fact]
    public void Read_the_user()
    {
        _character.Health = 240;
        _character.CurrentHealth = 100;
        _character.PowerType = PowerType.Energy;
        _character.Power = 100;
        _character.CurrentPower = 40;
        _character.Position = new Vector3(1, 2, 3);
        _character.Map = new MapId(2);
        _character.MarkCombat();

        ItemUseContext ctx = Context();

        Assert.Equal(("Tester7", (ushort)1, CharacterClass.Warrior), (ctx.Name, ctx.Level, ctx.Class));
        Assert.Equal((100u, 240u, PowerType.Energy, 40u, 100u), (ctx.Health, ctx.MaxHealth, ctx.PowerType, ctx.Power, ctx.MaxPower));
        Assert.Equal((new Vector3(1, 2, 3), (ushort)2), (ctx.Position, ctx.MapId));
        Assert.Equal((false, true, false, false), (ctx.InTown, ctx.InCombat, ctx.InParty, ctx.ReturningToTown));
    }

    [Fact]
    public void Read_the_item()
    {
        ItemTemplate tonic = new() { Id = new ItemTemplateId(700), Name = "Tonic", UseValue = 30, UseCooldownMs = 30000 };

        ItemUseContext ctx = Context(tonic);

        Assert.Equal((new ItemTemplateId(700), "Tonic", (uint?)30u, 3u, TimeSpan.FromSeconds(30)),
            (ctx.ItemId, ctx.ItemName, ctx.UseValue, ctx.StackCount, ctx.Cooldown));
        Assert.Equal(72u, ItemUsePercent.Of(240, 30));
        Assert.Equal(0u, ItemUsePercent.Of(240, null));
    }

    [Fact]
    public void Count_what_a_use_consumes_and_refuse_0_or_more_than_the_stack()
    {
        ItemUseContext ctx = Context();
        ctx.Consume();
        ctx.Consume(2);

        Assert.Equal(3u, ctx.Consumed);
        Assert.Throws<ArgumentOutOfRangeException>(() => ctx.Consume(0));
        Assert.Throws<InvalidOperationException>(() => ctx.Consume());
        Assert.Equal(3u, At(_character, InventoryType.Bag, 0).Count);   // taken by the service, not here
    }

    [Fact]
    public async Task Give_check_and_take_items_through_the_inventory_service()
    {
        StaticData data = await TestStaticData.LoadAsync(items: [Potion, Sword]);
        _world.Data.Returns(data);
        ItemUseContext ctx = Context();

        Assert.True(ctx.HasBagRoom(Sword.Id, 1));
        Assert.True(ctx.GiveItem(Sword.Id, 1));
        Assert.True(ctx.TakeItem(Potion.Id, 1));
        Assert.False(ctx.TakeItem(Relic.Id, 1));
        Assert.Equal(2u, At(_character, InventoryType.Bag, 0).Count);
        Assert.True(_character.ClientChanges.HasChanges);
    }

    [Fact]
    public void Give_and_take_money_through_the_wallet_and_never_past_the_cap()
    {
        ItemUseContext ctx = Context();

        Assert.True(ctx.TakeMoney(20));
        Assert.False(ctx.TakeMoney(1000));
        Assert.True(ctx.GiveMoney(5));
        Assert.Equal(35ul, ctx.Money);
        Assert.False(ctx.GiveMoney(new GameConfiguration().MaxMoney));
        Assert.Equal(35ul, ctx.Money);
        Assert.True(_character.SaveState.MoneyDirty);
    }

    [Fact]
    public void Heal_through_the_instance_and_restore_power_capped()
    {
        _host.RestoreHealth(_character, _character, 30).Returns(30u);
        _character.PowerType = PowerType.Mana;
        _character.Power = 100;
        _character.CurrentPower = 90;
        ItemUseContext ctx = Context();

        Assert.Equal(30u, ctx.RestoreHealth(30));
        Assert.Equal(0u, ctx.RestoreHealth(0));
        _host.Received(1).RestoreHealth(_character, _character, 30);
        Assert.Equal(10u, ctx.RestorePower(30));
        Assert.Equal(100u, _character.CurrentPower);
    }

    [Fact]
    public void Start_a_return_to_town_once_and_never_in_a_town()
    {
        ItemUseContext ctx = Context();

        Assert.True(ctx.ReturnToTown());
        Assert.True(_connection.RespawnInFlight);
        Assert.False(ctx.ReturnToTown());
        _resolver.ReceivedWithAnyArgs(1).ResolveTownAsync(default!, default);

        _connection.RespawnInFlight = false;
        _host.MapType.Returns(MapType.Town);
        Assert.False(ctx.ReturnToTown());
    }

    /// <summary>The move itself is MapTeleportShould's; here, that the context hands it over and its refusals come back.</summary>
    [Fact]
    public void Hand_a_teleport_to_the_map_teleport()
    {
        var town = Substitute.For<Avalon.World.Public.Instances.IMapInstance>();
        _world.MapTemplates.Returns(new List<MapTemplate>
        {
            new() { Id = new MapTemplateId(1), MapType = MapType.Town, Name = "town", Description = "" },
        });
        _world.InstanceRegistry.GetOrCreateTownInstanceAsync(new MapTemplateId(1), Arg.Any<ushort>())
            .Returns(Task.FromResult(town));
        ItemUseContext ctx = Context();

        Assert.False(ctx.Teleport(new MapTemplateId(9), new Vector3(1, 0, 1)));   // no such map
        _connection.DidNotReceiveWithAnyArgs().EnqueueContinuation(default(Task)!, default!);

        Assert.True(ctx.Teleport(new MapTemplateId(1), new Vector3(1, 0, 1)));
        Assert.True(ctx.ReturningToTown);                                          // the move is under way
        _connection.ReceivedWithAnyArgs(1).EnqueueContinuation(default(Task)!, default!);
        Assert.False(ctx.Teleport(new MapTemplateId(1)));                           // one move at a time
    }

    [Fact]
    public async Task Grant_experience_through_the_award_and_stop_at_the_level_cap()
    {
        StaticData data = await TestStaticData.LoadAsync(levels:
        [
            new CharacterLevelExperience { Level = 1, Experience = 100 },
            new CharacterLevelExperience { Level = 2, Experience = 500 },
            new CharacterLevelExperience { Level = 3, Experience = 900 },
        ]);
        _world.Data.Returns(data);

        Context().GrantExperience(100);
        Assert.Equal((ushort)2, _character.Level);

        _character.Level = 3;   // the highest row: the cap
        _character.Experience = 0;
        Context().GrantExperience(1000);
        Assert.Equal(((ushort)3, 0ul), (_character.Level, _character.Experience));
    }

    [Fact]
    public void Answer_quests_unavailable_when_the_world_has_no_quest_service()
    {
        ItemUseContext ctx = Context();

        Assert.Equal(QuestResult.NotAvailable, ctx.StartQuest(1));
        Assert.False(ctx.AdvanceQuest(1, 1));
    }

    [Fact]
    public void Spawn_a_creature_in_front_of_the_user_in_its_own_instance_and_no_farther_than_the_limit()
    {
        var creature = Substitute.For<ICreature>();
        creature.Guid.Returns(new Avalon.Common.ObjectGuid(Avalon.Common.ObjectType.Creature, 55));
        _placement.SpawnAt(_host, new CreatureTemplateId(4), Arg.Any<Vector3>()).Returns(creature);
        _character.Position = new Vector3(10, 0, 10);
        _character.Orientation = new Vector3(0, 0, 0);   // facing +Z

        ItemUseContext ctx = Context();

        Assert.Equal(creature.Guid, ctx.SpawnCreature(new CreatureTemplateId(4), 2f));
        _placement.Received(1).SpawnAt(_host, new CreatureTemplateId(4), new Vector3(10, 0, 12));
        _host.Received(1).DespawnAfter(creature, TimeSpan.FromMinutes(5));   // the default lifetime
        Assert.Equal(creature.Guid, ctx.SpawnCreature(new CreatureTemplateId(4), 3f, TimeSpan.FromSeconds(30)));
        _host.Received(1).DespawnAfter(creature, TimeSpan.FromSeconds(30));

        _placement.ClearReceivedCalls();
        Assert.Null(ctx.SpawnCreature(new CreatureTemplateId(4), ItemUseContext.MaxSpawnDistance + 0.1f));
        Assert.Null(ctx.SpawnCreature(new CreatureTemplateId(4), float.NaN));
        Assert.Null(ctx.SpawnCreature(new CreatureTemplateId(4), 2f, TimeSpan.Zero));
        _placement.DidNotReceiveWithAnyArgs().SpawnAt(default!, default!, default);
    }

    /// <summary>A summon never appears in a town.</summary>
    [Fact]
    public void Refuse_to_spawn_a_creature_in_a_town()
    {
        _host.MapType.Returns(MapType.Town);

        Assert.Null(Context().SpawnCreature(new CreatureTemplateId(4)));
        _placement.DidNotReceiveWithAnyArgs().SpawnAt(default!, default!, default);
    }

    [Fact]
    public void Leave_nothing_to_despawn_when_the_placement_found_no_ground()
    {
        _placement.SpawnAt(_host, new CreatureTemplateId(4), Arg.Any<Vector3>()).Returns((ICreature?)null);

        Assert.Null(Context().SpawnCreature(new CreatureTemplateId(4)));
        _host.DidNotReceiveWithAnyArgs().DespawnAfter(default!, default);
    }

    [Fact]
    public void Tell_and_whisper_to_the_user_only()
    {
        ItemUseContext ctx = Context();

        ctx.Tell("Hello.");
        ctx.Whisper("A voice", "Psst.");

        List<SChatMessagePacket> lines = TestTown.Read<SChatMessagePacket>(_sent, NetworkPacketType.SMSG_CHAT_MESSAGE);
        Assert.Equal([(ChatChannel.System, "System", "Hello."), (ChatChannel.Whisper, "A voice", "Psst.")],
            lines.Select(l => (l.Channel, l.CharacterName, l.Message)));
    }

    /// <summary>#763: a script's whisper and a told line come from no character, so both carry class 0.</summary>
    [Fact]
    public void Send_script_lines_with_class_0()
    {
        ItemUseContext ctx = Context();

        ctx.Tell("Hello.");
        ctx.Whisper("A voice", "Psst.");

        List<SChatMessagePacket> lines = TestTown.Read<SChatMessagePacket>(_sent, NetworkPacketType.SMSG_CHAT_MESSAGE);
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Equal((ushort)0, l.CharacterClass));
    }

    [Fact]
    public void Offer_no_party_members_to_a_user_in_no_party()
    {
        Assert.Empty(Context().PartyMembersHere());
        Assert.Equal(0u, Context().RestoreHealthOf(new Avalon.Common.ObjectGuid(Avalon.Common.ObjectType.Character, 8), 10));
    }
}
