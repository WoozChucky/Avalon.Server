using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.ChunkLayouts;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Maps;
using Avalon.World.Scripts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// #640: a busy instance's steady tick. Between broadcasts it allocates nothing; on a broadcast it
/// allocates the packets it sends and nothing per entity described in them.
/// </summary>
public class MapInstanceTickAllocationShould
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1d / 60d);

    /// <summary>
    /// What every packet costs however much it holds, beyond its payload: the packet, its encryption
    /// callback, the NetworkPacket and its header.
    /// </summary>
    private const long PerPacket = 256;

    private const int CreatureCount = 50;

    private static (MapInstance Instance, QuietConnection[] Players, List<Creature> Creatures) Build()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new CombatConfig());
        services.AddSingleton(Substitute.For<IScriptManager>());

        var entryChunk = new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero);
        var layout = new ChunkLayout(Seed: 0, Chunks: [entryChunk], EntryChunk: entryChunk, BossChunk: null,
            Portals: [], EntrySpawnWorldPos: Vector3.zero, CellSize: 30f, Config: null);

        // No reference data: a tick with no shop open and no kill never reads it.
        var instance = new MapInstance(NullLoggerFactory.Instance, services.BuildServiceProvider(),
            new QuietWorld(null!), new MapTemplateId(1), ownerCharacterId: null, layout,
            Substitute.For<IMapNavigator>(), seed: 0);

        var players = new QuietConnection[2];
        for (int p = 0; p < players.Length; p++)
        {
            CharacterEntity character = TestCharacters.New((uint)(640_001 + p));
            character.Spells.Load(Array.Empty<IAbility>());
            character.Position = new Vector3(p, 0f, 0f);
            players[p] = new QuietConnection(character);
            instance.AddCharacter(players[p]);
        }

        // A real template: a substituted one allocates on every read the broadcast makes of it.
        var template = new CreatureTemplate { Id = new CreatureTemplateId(4), Name = "Wolf" };
        var creatures = new List<Creature>();
        for (int i = 0; i < CreatureCount; i++)
        {
            var creature = new Creature
            {
                Guid = new ObjectGuid(ObjectType.Creature, (uint)(64_000 + i)),
                TemplateId = template.Id,
                Metadata = template,
                Name = "Wolf",
                Position = new Vector3(i % 10, 0f, i / 10),
                Health = 100,
                CurrentHealth = 100,
            };
            instance.AddCreature(creature);
            creatures.Add(creature);
        }

        return (instance, players, creatures);
    }

    /// <summary>Every creature moves every tick, so every broadcast describes every one of them.</summary>
    private static void Walk(List<Creature> creatures, int tick)
    {
        float step = (tick % 60) * 0.01f;
        for (int i = 0; i < creatures.Count; i++)
            creatures[i].Position = new Vector3(i % 10 + step, 0f, i / 10);
    }

    private static long SentPayloadBytes(QuietConnection[] players)
    {
        long bytes = 0;
        foreach (QuietConnection player in players)
            bytes += player.SentPayloadBytes;
        return bytes;
    }

    private static int Sent(QuietConnection[] players)
    {
        int sent = 0;
        foreach (QuietConnection player in players)
            sent += player.Sent;
        return sent;
    }

    [Fact]
    public void Tick_between_broadcasts_without_allocating()
    {
        var (instance, players, creatures) = Build();
        for (int tick = 0; tick < 120; tick++)
        {
            Walk(creatures, tick);
            instance.Update(Tick);
        }

        // The fewest bytes over three windows of the five ticks after a broadcast.
        long fewest = long.MaxValue;
        for (int window = 0; window < 3; window++)
        {
            // Run up to a broadcast, which sends to every player.
            int sent = Sent(players);
            int tick = 0;
            while (Sent(players) == sent)
            {
                Walk(creatures, tick++);
                instance.Update(Tick);
            }

            int afterBroadcast = Sent(players);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int quiet = 0; quiet < 4; quiet++)
            {
                Walk(creatures, quiet);
                instance.Update(Tick);
            }

            fewest = Math.Min(fewest, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(afterBroadcast, Sent(players));
        }

        Assert.Equal(0, fewest);
    }

    [Fact]
    public void Broadcast_allocating_only_the_packets_it_sends()
    {
        var (instance, players, creatures) = Build();
        for (int tick = 0; tick < 120; tick++)
        {
            Walk(creatures, tick);
            instance.Update(Tick);
        }

        // The fewest bytes over three windows, each of six broadcasts, beyond what the sent packets cost.
        long fewest = long.MaxValue;
        for (int window = 0; window < 3; window++)
        {
            int sentBefore = Sent(players);
            long payloadBefore = SentPayloadBytes(players);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int tick = 0; tick < 36; tick++)
            {
                Walk(creatures, tick);
                instance.Update(Tick);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            int sent = Sent(players) - sentBefore;
            long payload = SentPayloadBytes(players) - payloadBefore;

            // Each broadcast describes every creature to each player (an update each).
            Assert.InRange(sent, 6 * players.Length - players.Length, 6 * players.Length + players.Length);
            Assert.True(payload >= sent * CreatureCount * 10L, $"payload {payload} for {sent} packets");
            fewest = Math.Min(fewest, allocated - payload - sent * PerPacket);
        }

        Assert.InRange(fewest, long.MinValue, 0);
    }
}
