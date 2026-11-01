using System.Reflection;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.State;
using Avalon.World.ChunkLayouts;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public;
using Microsoft.Extensions.Time.Testing;
using ProtoBuf;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// #982: a client's view is decided over its instance's spatial index, the cells within radius + margin of its character,
/// and must be the view a scan of every member decides. Two instances are driven side by side through the same seeded
/// changes, one over the index and one scanning everything (<c>MapInstance.DecideViewsByFullScan</c>), and after every
/// tick each client must have been told of the same objects coming, changing and going, and hold the same ones.
/// </summary>
/// <remarks>
/// The changes are the ones the index can get wrong: moves within and across cells, positions on cell edges and in the
/// 50 to 60 m margin of a client, teleports off the layout (in the clamped edge cells, a client and others together) and
/// back, positions that are not finite (the client's own character among them, which it always sees), writes through
/// <see cref="IWorldObject.Position" />, characters leaving, coming back and entering a second instance without leaving
/// this one, creatures despawning, spawning (placed before or after they join), joining again, and replaced by another
/// object under the same guid. Within a kind the index lists objects in its cells' order (the owner's decision), so what
/// a client is told each tick is compared as sets. Ids 982_xxx are unique to this class.
/// </remarks>
public class SpatialVisibilityShould
{
    private const int Ticks = 600;
    private const float Cell = 30f;
    private static readonly TimeSpan s_tick = TimeSpan.FromSeconds(1d / 60d);

    // A forest-like layout: a path of 30 m chunks with a branch, so most of the grid's cells are off the layout.
    private static readonly (short X, short Z)[] s_chunks =
        [(0, 0), (1, 0), (2, 0), (2, 1), (3, 1), (4, 1), (4, 2), (4, 3), (5, 3), (6, 3), (1, -1), (1, -2)];

    // Far off the layout, each a place a client and the objects with it are sent together.
    private static readonly Vector3[] s_offLayout =
        [new(-400f, 0f, 50f), new(900f, 3f, -300f), new(1e6f, 0f, 1e6f), new(-3e7f, 0f, 20f)];

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Decide_every_view_as_a_scan_of_every_member_does(int seed)
    {
        var random = new Random(seed);
        ChunkLayout layout = Layout();
        using MapInstance indexed = TestMapInstances.Build(NewWorld(), time: _clock, layout: layout);
        using MapInstance scanned = TestMapInstances.Build(NewWorld(), time: _clock, layout: layout);
        typeof(MapInstance).GetMethod("DecideViewsByFullScan", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(scanned, null);

        // A second instance on each side, which a character can enter without leaving the first.
        using MapInstance indexedElsewhere = TestMapInstances.Build(NewWorld(), time: _clock, layout: layout);
        using MapInstance scannedElsewhere = TestMapInstances.Build(NewWorld(), time: _clock, layout: layout);

        var characters = new List<Twin<MapInstanceClient>>();
        var creatures = new List<Twin<Creature>>();
        uint nextCreature = 982_100;
        for (uint id = 982_001; id <= 982_008; id++)
        {
            var twin = new Twin<MapInstanceClient>(Join(indexed, id), Join(scanned, id)) { In = true };
            characters.Add(twin);
            Place(twin, OnLayout(random), random);
        }

        for (int i = 0; i < 40; i++)
            creatures.Add(Spawn(indexed, scanned, nextCreature++, OnLayout(random), random));

        for (int tick = 1; tick <= Ticks; tick++)
        {
            Change(random, indexed, scanned, indexedElsewhere, scannedElsewhere, characters, creatures, ref nextCreature);

            int[] from = characters.Select(c => c.Indexed.Sent.Count).ToArray();
            int[] fromScanned = characters.Select(c => c.Scanned.Sent.Count).ToArray();
            indexed.Update(s_tick);
            scanned.Update(s_tick);

            for (int i = 0; i < characters.Count; i++)
            {
                Twin<MapInstanceClient> watcher = characters[i];
                string at = $"seed {seed}, tick {tick}, client {watcher.Indexed.Character.Guid.Id}";
                Assert.Equal(Told(watcher.Scanned.Sent.Skip(fromScanned[i])), Told(watcher.Indexed.Sent.Skip(from[i])));
                foreach (ulong guid in characters.Select(c => c.Indexed.Character.Guid.RawValue)
                             .Concat(creatures.Select(c => c.Indexed.Guid.RawValue)))
                {
                    var objectGuid = new ObjectGuid(guid);
                    Assert.True(watcher.Scanned.Character.CharacterGameState.Knows(objectGuid)
                                == watcher.Indexed.Character.CharacterGameState.Knows(objectGuid),
                        $"{at}: the client's view holds {new ObjectGuid(guid)} on one side only");
                }
            }
        }
    }

    private static void Change(Random random, MapInstance indexed, MapInstance scanned, MapInstance indexedElsewhere,
        MapInstance scannedElsewhere, List<Twin<MapInstanceClient>> characters, List<Twin<Creature>> creatures,
        ref uint nextCreature)
    {
        // Small steps, most of them within a cell, some across.
        foreach (Twin<Creature> creature in creatures)
        {
            if (random.NextDouble() < 0.3)
                Move(creature, Step(creature.Indexed.Position, random), random);
        }

        foreach (Twin<MapInstanceClient> character in characters)
        {
            if (random.NextDouble() < 0.3)
                Place(character, Step(character.Indexed.Character.Position, random), random);
        }

        // Three moves a tick to where the index can go wrong.
        for (int i = 0; i < 3; i++)
        {
            Vector3 watcher = characters[random.Next(characters.Count)].Indexed.Character.Position;
            Vector3 target = random.Next(5) switch
            {
                0 => InTheMargin(watcher, random),
                1 => OnACellEdge(random),
                2 => s_offLayout[random.Next(s_offLayout.Length)] + new Vector3(Offset(random), 0f, Offset(random)),
                3 => NotFinite(random),
                _ => OnLayout(random),
            };

            if (random.Next(3) == 0)
                Place(characters[random.Next(characters.Count)], target, random);
            else
                Move(creatures[random.Next(creatures.Count)], target, random);
        }

        // Membership, now and then.
        switch (random.Next(40))
        {
            case 0 when creatures.Count > 10:
            {
                Twin<Creature> despawned = creatures[random.Next(creatures.Count)];
                indexed.RemoveCreature(despawned.Indexed);
                scanned.RemoveCreature(despawned.Scanned);
                creatures.Remove(despawned);
                break;
            }
            case 1:
                creatures.Add(Spawn(indexed, scanned, nextCreature++, OnLayout(random), random));
                break;
            case 2:
            {
                // Joins again while a member: nothing changes.
                Twin<Creature> again = creatures[random.Next(creatures.Count)];
                indexed.AddCreature(again.Indexed);
                scanned.AddCreature(again.Scanned);
                break;
            }
            case 3:
            {
                // Another object under the same guid takes its place.
                int at = random.Next(creatures.Count);
                Twin<Creature> old = creatures[at];
                creatures[at] = Spawn(indexed, scanned, old.Indexed.Guid.Id, OnLayout(random), random);
                break;
            }
            case 4:
            {
                Twin<MapInstanceClient> character = characters[random.Next(characters.Count)];
                if (character.In)
                {
                    indexed.RemoveCharacter(character.Indexed.Connection);
                    scanned.RemoveCharacter(character.Scanned.Connection);
                }
                else
                {
                    indexed.AddCharacter(character.Indexed.Connection);
                    scanned.AddCharacter(character.Scanned.Connection);
                }

                character.In = !character.In;
                break;
            }
            case 5:
            {
                // Enters a second instance without leaving the first, or leaves the second.
                Twin<MapInstanceClient> character = characters[random.Next(characters.Count)];
                if (character.Elsewhere)
                {
                    indexedElsewhere.RemoveCharacter(character.Indexed.Connection);
                    scannedElsewhere.RemoveCharacter(character.Scanned.Connection);
                }
                else
                {
                    indexedElsewhere.AddCharacter(character.Indexed.Connection);
                    scannedElsewhere.AddCharacter(character.Scanned.Connection);
                }

                character.Elsewhere = !character.Elsewhere;
                break;
            }
            case 6:
            {
                Twin<Creature> dead = creatures[random.Next(creatures.Count)];
                dead.Indexed.CurrentHealth = 0;
                dead.Scanned.CurrentHealth = 0;
                break;
            }
        }
    }

    private static Twin<Creature> Spawn(MapInstance indexed, MapInstance scanned, uint id, Vector3 position, Random random)
    {
        // Placed before it joins, or after.
        bool placedFirst = random.Next(2) == 0;
        var twin = new Twin<Creature>(NewCreature(id, placedFirst ? position : Vector3.zero),
            NewCreature(id, placedFirst ? position : Vector3.zero));
        indexed.AddCreature(twin.Indexed);
        scanned.AddCreature(twin.Scanned);
        if (!placedFirst)
            Move(twin, position, random);
        return twin;
    }

    private static Creature NewCreature(uint id, Vector3 position) => new()
    {
        Guid = new ObjectGuid(ObjectType.Creature, id),
        Metadata = Loot.LootTestData.BoarTemplate(null),
        Position = position,
        Level = 1,
        Health = 100,
        CurrentHealth = 100,
    };

    // Through the concrete setter, or through IWorldObject's, which lands there.
    private static void Move(Twin<Creature> creature, Vector3 position, Random random)
    {
        if (random.Next(2) == 0)
        {
            creature.Indexed.Position = position;
            creature.Scanned.Position = position;
        }
        else
        {
            ((IWorldObject)creature.Indexed).Position = position;
            ((IWorldObject)creature.Scanned).Position = position;
        }
    }

    private static void Place(Twin<MapInstanceClient> character, Vector3 position, Random random)
    {
        if (random.Next(2) == 0)
        {
            character.Indexed.Character.Position = position;
            character.Scanned.Character.Position = position;
        }
        else
        {
            ((IWorldObject)character.Indexed.Character).Position = position;
            ((IWorldObject)character.Scanned.Character).Position = position;
        }
    }

    private static Vector3 Step(Vector3 from, Random random) =>
        float.IsFinite(from.x) && float.IsFinite(from.y) && float.IsFinite(from.z) && Math.Abs(from.x) < 1e5f
            ? new Vector3(from.x + (float)(random.NextDouble() * 6 - 3), from.y, from.z + (float)(random.NextDouble() * 6 - 3))
            : OnLayout(random);

    // On the layout, or within 40 m of it.
    private static Vector3 OnLayout(Random random) => new(
        (float)(random.NextDouble() * 290 - 40), (float)(random.NextDouble() * 4), (float)(random.NextDouble() * 220 - 100));

    // Between the radius and radius + margin of a client, or on either edge, along an axis or not.
    private static Vector3 InTheMargin(Vector3 watcher, Random random)
    {
        float distance = random.Next(7) switch
        {
            0 => 50f,
            1 => 60f,
            2 => MathF.BitDecrement(50f),
            3 => MathF.BitIncrement(50f),
            4 => MathF.BitDecrement(60f),
            5 => MathF.BitIncrement(60f),
            _ => (float)(45 + random.NextDouble() * 20),
        };
        double angle = random.Next(2) == 0 ? random.Next(4) * Math.PI / 2 : random.NextDouble() * 2 * Math.PI;
        return new Vector3(watcher.x + distance * (float)Math.Cos(angle), watcher.y,
            watcher.z + distance * (float)Math.Sin(angle));
    }

    // On the line between two cells, or a float either side of it.
    private static Vector3 OnACellEdge(Random random)
    {
        return new Vector3(Edge(random.Next(-1, 9)), 0f, Edge(random.Next(-3, 6)));

        float Edge(int line) => random.Next(3) switch
        {
            0 => line * Cell,
            1 => MathF.BitDecrement(line * Cell),
            _ => MathF.BitIncrement(line * Cell),
        };
    }

    private static Vector3 NotFinite(Random random)
    {
        float bad = random.Next(3) switch { 0 => float.NaN, 1 => float.PositiveInfinity, _ => float.NegativeInfinity };
        return random.Next(3) switch
        {
            0 => new Vector3(bad, 0f, 10f),
            1 => new Vector3(10f, bad, 10f),
            _ => new Vector3(10f, 0f, bad),
        };
    }

    private static float Offset(Random random) => (float)(random.NextDouble() * 100 - 50);

    private static ChunkLayout Layout()
    {
        var chunks = s_chunks
            .Select(c => new PlacedChunk(new ChunkTemplateId(1), c.X, c.Z, 0, new Vector3(c.X * Cell, 0f, c.Z * Cell)))
            .ToList();
        return new ChunkLayout(0, chunks, chunks[0], null, [], Vector3.zero, Cell);
    }

    /// <summary>What these packets told their client was added, updated and removed, each as a sorted set of guids.</summary>
    private static string Told(IEnumerable<OutboundPacket> packets)
    {
        var added = new SortedSet<ulong>();
        var updated = new SortedSet<ulong>();
        var removed = new SortedSet<ulong>();
        foreach (OutboundPacket packet in packets)
        {
            switch (packet.Header.Type)
            {
                case NetworkPacketType.SMSG_WORLD_STATE_ADD:
                    added.UnionWith(Decode<SInstanceStateAddPacket>(packet).Adds?.Select(s => s.Guid) ?? []);
                    break;
                case NetworkPacketType.SMSG_WORLD_STATE_UPDATE:
                    updated.UnionWith(Decode<SInstanceStateUpdatePacket>(packet).Updates?.Select(s => s.Guid) ?? []);
                    break;
                case NetworkPacketType.SMSG_WORLD_STATE_REMOVE:
                    removed.UnionWith(Decode<SInstanceStateRemovePacket>(packet).Removes ?? []);
                    break;
            }
        }

        return $"added [{string.Join(", ", added)}], updated [{string.Join(", ", updated)}], removed [{string.Join(", ", removed)}]";
    }

    private static T Decode<T>(OutboundPacket packet)
    {
        using var stream = new MemoryStream(packet.PayloadMemory.ToArray());
        return Serializer.Deserialize<T>(stream);
    }

    /// <summary>One object on each side: in the instance over the index, and in the one that scans.</summary>
    private sealed class Twin<T>(T indexed, T scanned)
    {
        public T Indexed { get; } = indexed;
        public T Scanned { get; } = scanned;
        public bool In { get; set; }
        public bool Elsewhere { get; set; }
    }
}
