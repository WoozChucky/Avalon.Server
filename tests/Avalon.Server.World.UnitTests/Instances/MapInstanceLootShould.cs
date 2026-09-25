using System.IO;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Loot;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Loot;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Maps;
using Avalon.World.Public.Units;
using Avalon.World.Reload;
using Avalon.World.Scripts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ProtoBuf;
using Xunit;
using static Avalon.Server.World.UnitTests.Loot.LootTestData;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// A real kill through a real MapInstance. The roller, allocator and placement each have their own
/// tests; these pin the wiring, which is the only thing that fails if OnCreatureKilled stops
/// calling them.
/// </summary>
public class MapInstanceLootShould
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1d / 60d);

    private List<LootTable> _tables = [Table(1, Item(1, Sword))];
    private StaticData? _data;

    private sealed record Client(IWorldConnection Connection, CharacterEntity Character, List<NetworkPacket> Sent)
    {
        public List<SLootSpawnedPacket> Spawned() => Read<SLootSpawnedPacket>(NetworkPacketType.SMSG_LOOT_SPAWNED);

        public List<SLootDespawnedPacket> Despawned() => Read<SLootDespawnedPacket>(NetworkPacketType.SMSG_LOOT_DESPAWNED);

        private List<T> Read<T>(NetworkPacketType type) => Sent
            .Where(p => p.Header.Type == type)
            .Select(p =>
            {
                using var stream = new MemoryStream(p.Payload);
                return Serializer.Deserialize<T>(stream);
            })
            .ToList();
    }

    private static Client Join(MapInstance instance, uint id)
    {
        CharacterEntity character = Inventory.TestCharacters.New(id);
        character.Spells.Load(Array.Empty<IAbility>());   // the tick updates abilities; an unloaded list throws

        var sent = new List<NetworkPacket>();
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        connection.When(c => c.Send(Arg.Any<NetworkPacket>())).Do(ci => sent.Add(ci.Arg<NetworkPacket>()));

        instance.AddCharacter(connection);
        return new Client(connection, character, sent);
    }

    private async Task<MapInstance> Build(uint? owner = 7, ILootRoller? roller = null)
    {
        StaticData data = await LootStaticData.LoadAsync(() => Items, () => _tables);
        _data = data;

        var world = Substitute.For<IWorld>();
        world.Configuration.Returns(new GameConfiguration());
        world.MapTemplates.Returns(new List<MapTemplate>());
        world.Data.Returns(data);

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IScriptManager)).Returns(Substitute.For<IScriptManager>());
        serviceProvider.GetService(typeof(CombatConfig)).Returns(new CombatConfig());
        serviceProvider.GetService(typeof(ILootRoller))
            .Returns(roller ?? new LootRoller(new LootRandom(new Random(460)), NullLogger<LootRoller>.Instance));
        serviceProvider.GetService(typeof(ILootAllocator))
            .Returns(new InstanceOwnerLootAllocator(Options.Create(new GameConfiguration()), new FixedTimeProvider(Now)));

        // Open, flat ground: every ring point is reachable and the height search returns its centre.
        var navigator = Substitute.For<IMapNavigator>();
        navigator.RaycastWalkable(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns(ci => ci.ArgAt<Vector3>(1));
        navigator.SampleGroundHeight(Arg.Any<float>(), Arg.Any<float>(), Arg.Any<float>())
            .Returns(ci => ci.ArgAt<float>(1));

        var entryChunk = new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero);
        var layout = new ChunkLayout(Seed: 0, Chunks: [entryChunk], EntryChunk: entryChunk, BossChunk: null,
            Portals: [], EntrySpawnWorldPos: Vector3.zero, CellSize: 30f, Config: null);

        return new MapInstance(NullLoggerFactory.Instance, serviceProvider, world, new MapTemplateId(2),
            owner, layout, navigator, seed: 0);
    }

    private static Creature Kill(MapInstance instance, uint id, IUnit? killer = null, CreatureTemplate? template = null)
    {
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            Metadata = template ?? BoarTemplate(1, minGold: 5, maxGold: 5),
            Position = new Vector3(10f, 2f, 10f),
            Experience = 50,
        };
        instance.AddCreature(creature);
        creature.Died(killer ?? Substitute.For<IUnit>());
        return creature;
    }

    [Fact]
    public async Task Drop_A_Kills_Loot_And_Tell_Everyone_In_One_Packet()
    {
        using MapInstance instance = await Build();
        Client first = Join(instance, 460_101);
        Client second = Join(instance, 460_102);

        Kill(instance, 460_001);

        // One sword from table 1, then the 5-copper pile.
        Assert.Equal(2, instance.Drops.Count);
        foreach (Client client in new[] { first, second })
        {
            SLootSpawnedPacket spawned = Assert.Single(client.Spawned());
            Assert.Equal(2, spawned.Drops.Count);
            Assert.Equal(Sword.Id.Value, spawned.Drops[0].ItemTemplateId);
            Assert.Equal(5UL, spawned.Drops[1].Gold);
        }
    }

    [Fact]
    public async Task Reserve_The_Drops_For_The_Instance_Owner()
    {
        using MapInstance instance = await Build(owner: 7);

        Kill(instance, 460_001);

        Assert.Equal(2, instance.Drops.Count);   // the sword and the pile: Assert.All passes on nothing
        Assert.All(instance.Drops.All, drop =>
        {
            Assert.Equal(7u, drop.OwnerCharacterId);
            Assert.Equal(Now.UtcDateTime + TimeSpan.FromSeconds(30), drop.FreeForAllAt);
        });
    }

    [Fact]
    public async Task Drop_Nothing_And_Send_Nothing_For_A_Kill_That_Rolls_Nothing()
    {
        using MapInstance instance = await Build();
        Client client = Join(instance, 460_101);
        instance.Update(Tick);

        // No table and no gold: the roll is empty.
        Kill(instance, 460_003, template: BoarTemplate(null, minGold: 0, maxGold: 0));
        instance.Update(Tick);

        Assert.Equal(0, instance.Drops.Count);
        Assert.Empty(client.Spawned());
    }

    [Fact]
    public async Task Tell_A_Character_Who_Enters_About_The_Drops_Already_There_On_The_Next_Tick()
    {
        using MapInstance instance = await Build();
        Client first = Join(instance, 460_101);
        instance.Update(Tick);            // first's own snapshot: nothing on the ground yet, so nothing sent
        Kill(instance, 460_001);

        Client second = Join(instance, 460_102);
        Assert.Empty(second.Spawned());   // not from AddCharacter: the map transition packets go first

        instance.Update(Tick);

        SLootSpawnedPacket snapshot = Assert.Single(second.Spawned());
        Assert.Equal(2, snapshot.Drops.Count);
        Assert.Single(first.Spawned());   // the kill broadcast only; first is owed no snapshot
    }

    [Fact]
    public async Task Send_No_Snapshot_To_A_Character_Entering_An_Instance_With_Nothing_On_The_Ground()
    {
        using MapInstance instance = await Build();
        Client client = Join(instance, 460_101);

        instance.Update(Tick);

        Assert.Empty(client.Spawned());
    }

    [Fact]
    public async Task Send_No_Snapshot_To_A_Character_Who_Left_Before_The_Tick()
    {
        using MapInstance instance = await Build();
        Client stays = Join(instance, 460_101);
        instance.Update(Tick);
        Kill(instance, 460_001);
        Client leaves = Join(instance, 460_102);

        instance.RemoveCharacter(leaves.Connection);
        instance.Update(Tick);

        // It joined after the kill, so no broadcast reached it, and it left before its snapshot.
        Assert.Empty(leaves.Spawned());
        Assert.Single(stays.Spawned());
    }

    [Fact]
    public async Task Tell_Everyone_When_Drops_Leave_The_Ground()
    {
        using MapInstance instance = await Build();
        Client first = Join(instance, 460_101);
        Client second = Join(instance, 460_102);
        Kill(instance, 460_001);
        ObjectGuid[] guids = instance.Drops.All.Select(d => d.Guid).ToArray();

        instance.BroadcastLootDespawned(guids);

        foreach (Client client in new[] { first, second })
            Assert.Equal(guids.Select(g => g.RawValue), Assert.Single(client.Despawned()).LootGuids);
    }

    [Fact]
    public async Task Remove_Every_Drop_When_The_Instance_Is_Disposed()
    {
        MapInstance instance = await Build();   // disposed below: that is what this test does
        Kill(instance, 460_001);
        Assert.NotEqual(0, instance.Drops.Count);

        instance.Dispose();

        Assert.Equal(0, instance.Drops.Count);
    }

    [Fact]
    public async Task Still_Award_Experience_When_Rolling_Loot_Throws()
    {
        var roller = Substitute.For<ILootRoller>();
        roller.Roll(Arg.Any<CreatureTemplate>(), Arg.Any<LootCatalog>(), Arg.Any<IReadOnlyCollection<ItemTemplate>>())
            .Returns(_ => throw new InvalidOperationException("bad table"));
        using MapInstance instance = await Build(roller: roller);

        ICharacter killer = Substitute.For<ICharacter>();
        killer.Guid.Returns(new ObjectGuid(ObjectType.Character, 7));
        killer.Level.Returns((ushort)1);
        killer.Experience.Returns(0ul);

        Kill(instance, 460_001, killer);

        Assert.Equal(0, instance.Drops.Count);
        killer.Received().Experience = 50;
    }

    [Fact]
    public async Task Use_Reloaded_Tables_For_The_Next_Kill_And_Leave_Drops_On_The_Ground_Alone()
    {
        using MapInstance instance = await Build();
        StaticData data = _data!;
        Kill(instance, 460_001);
        GroundLoot[] before = instance.Drops.All.ToArray();

        _tables = [Table(1, Item(1, Staff))];
        data.Apply(await data.PrepareAsync(ReloadArea.Loot));
        Kill(instance, 460_002);

        Assert.All(before, drop => Assert.True(instance.Drops.TryGet(drop.Guid, out GroundLoot? still) && ReferenceEquals(drop, still)));
        Assert.Contains(before, d => d.ItemTemplateId == Sword.Id);
        GroundLoot[] after = instance.Drops.All.Except(before).ToArray();
        Assert.Contains(after, d => d.ItemTemplateId == Staff.Id);
        Assert.DoesNotContain(after, d => d.ItemTemplateId == Sword.Id);
    }
}
