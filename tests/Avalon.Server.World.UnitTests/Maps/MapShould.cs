using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.World;
using Avalon.World.Maps;
using Avalon.World.Pools;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Maps;
using Avalon.World.Scripts;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Maps;

public class MapShould
{
    private static Map MakeEmptyMap()
    {
        var map = new Map(NullLoggerFactory.Instance);
        map.Id = new MapId(1);
        map.Chunks = Array.Empty<Chunk>();
        return map;
    }

    private static IWorldConnection MakeNullCharConnection()
    {
        var conn = Substitute.For<IWorldConnection>();
        conn.Character.Returns((ICharacter?)null);
        return conn;
    }

    private static IWorldConnection MakeConnection(Vector3 position)
    {
        var conn = Substitute.For<IWorldConnection>();
        var character = Substitute.For<ICharacter>();
        character.Position.Returns(position);
        character.Name.Returns("TestPlayer");
        conn.Character.Returns(character);
        return conn;
    }

    // ──────────────────────────────────────────────
    // AddPlayer guards
    // ──────────────────────────────────────────────

    [Fact]
    public void AddPlayer_ThrowsInvalidOperation_WhenCharacterIsNull()
    {
        var map = MakeEmptyMap();

        Assert.Throws<InvalidOperationException>(() => map.AddPlayer(MakeNullCharConnection()));
    }

    [Fact]
    public void AddPlayer_ThrowsInvalidOperation_WhenNoChunkContainsPosition()
    {
        var map = MakeEmptyMap(); // no chunks → GetChunk returns null
        var conn = MakeConnection(new Vector3(50, 0, 50));

        Assert.Throws<InvalidOperationException>(() => map.AddPlayer(conn));
    }

    // ──────────────────────────────────────────────
    // RemovePlayer guards
    // ──────────────────────────────────────────────

    [Fact]
    public void RemovePlayer_ThrowsInvalidOperation_WhenCharacterIsNull()
    {
        var map = MakeEmptyMap();

        Assert.Throws<InvalidOperationException>(() => map.RemovePlayer(MakeNullCharConnection()));
    }

    [Fact]
    public void RemovePlayer_ThrowsInvalidOperation_WhenNoChunkContainsPosition()
    {
        var map = MakeEmptyMap();
        var conn = MakeConnection(new Vector3(50, 0, 50));

        Assert.Throws<InvalidOperationException>(() => map.RemovePlayer(conn));
    }

    // ──────────────────────────────────────────────
    // FindCreature
    // ──────────────────────────────────────────────

    [Fact]
    public void FindCreature_ReturnsNull_WhenNoChunks()
    {
        var map = MakeEmptyMap();

        var result = map.FindCreature(new CreatureId(1));

        Assert.Null(result);
    }

    [Fact]
    public void FindCreature_ReturnsNull_WhenCreatureNotInAnyChunk()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        var scriptManager = Substitute.For<Avalon.World.Scripts.IScriptManager>();
        serviceProvider.GetService(typeof(Avalon.World.Scripts.IScriptManager)).Returns(scriptManager);

        var chunk = new Chunk(NullLoggerFactory.Instance, serviceProvider, 1, Avalon.Common.Mathematics.Vector2.zero,
            Substitute.For<Avalon.World.Pools.IPoolManager>(), Substitute.For<IWorld>())
        {
            Metadata = new Avalon.World.Public.Maps.ChunkMetadata
            {
                Name = "C1", Position = new Vector3(0, 0, 0), Size = new Vector3(100, 10, 100),
                Creatures = new List<Avalon.World.Public.Maps.CreatureInfo>(),
                Trees = new List<Avalon.World.Public.Maps.TreeInfo>(),
                MeshFile = "", GeometryFile = ""
            },
            Id = new ChunkId(1u)
        };

        var map = new Map(NullLoggerFactory.Instance);
        map.Id = new MapId(1);
        map.Chunks = new[] { chunk };

        // CreatureId 999 not added to any chunk
        var result = map.FindCreature(new CreatureId(999));

        Assert.Null(result);
    }

    [Fact]
    public void FindCreature_ReturnsCreature_WhenPresentInChunk()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        var scriptManager = Substitute.For<Avalon.World.Scripts.IScriptManager>();
        serviceProvider.GetService(typeof(Avalon.World.Scripts.IScriptManager)).Returns(scriptManager);

        var chunk = new Chunk(NullLoggerFactory.Instance, serviceProvider, 1, Avalon.Common.Mathematics.Vector2.zero,
            Substitute.For<Avalon.World.Pools.IPoolManager>(), Substitute.For<IWorld>())
        {
            Metadata = new Avalon.World.Public.Maps.ChunkMetadata
            {
                Name = "C1", Position = new Vector3(0, 0, 0), Size = new Vector3(100, 10, 100),
                Creatures = new List<Avalon.World.Public.Maps.CreatureInfo>(),
                Trees = new List<Avalon.World.Public.Maps.TreeInfo>(),
                MeshFile = "", GeometryFile = ""
            },
            Id = new ChunkId(1u)
        };

        var creature = Substitute.For<Avalon.World.Public.Creatures.ICreature>();
        creature.Guid.Returns(new Avalon.Common.ObjectGuid(Avalon.Common.ObjectType.Creature, 42u));
        chunk.AddCreature(creature);

        var map = new Map(NullLoggerFactory.Instance);
        map.Id = new MapId(1);
        map.Chunks = new[] { chunk };

        var result = map.FindCreature(new CreatureId(42));

        Assert.NotNull(result);
        Assert.Equal(creature.Guid, result!.Guid);
    }

    // ──────────────────────────────────────────────
    // SpawnStartingEntities
    // ──────────────────────────────────────────────

    [Fact]
    public void SpawnStartingEntities_DoesNotThrow_WhenChunksEmpty()
    {
        var map = MakeEmptyMap();
        var ex = Record.Exception(() => map.SpawnStartingEntities());
        Assert.Null(ex);
    }

    // ──────────────────────────────────────────────
    // DetectNeighbors
    // ──────────────────────────────────────────────

    private static Chunk MakeChunk(Vector3 position, Vector3 size, uint chunkId = 1)
    {
        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(IScriptManager)).Returns(Substitute.For<IScriptManager>());

        return new Chunk(NullLoggerFactory.Instance, sp, mapId: 1,
            Avalon.Common.Mathematics.Vector2.zero,
            Substitute.For<IPoolManager>(), Substitute.For<IWorld>())
        {
            Id = new ChunkId(chunkId),
            Metadata = new ChunkMetadata
            {
                Name = $"Chunk{chunkId}",
                Position = position,
                Size = size,
                Creatures = [],
                Trees = [],
                MeshFile = "",
                GeometryFile = ""
            }
        };
    }

    [Fact]
    public void DetectNeighbors_DoesNotThrow_WithSingleChunk()
    {
        var map = new Map(NullLoggerFactory.Instance);
        map.Id = new MapId(1);
        map.Chunks = new[] { MakeChunk(Vector3.zero, new Vector3(100, 10, 100), 1) };

        var ex = Record.Exception(() => map.DetectNeighbors());
        Assert.Null(ex);
    }

    [Fact]
    public void DetectNeighbors_SingleChunk_HasNoNeighbors()
    {
        var chunk = MakeChunk(Vector3.zero, new Vector3(100, 10, 100), 1);
        var map = new Map(NullLoggerFactory.Instance);
        map.Id = new MapId(1);
        map.Chunks = new[] { chunk };

        map.DetectNeighbors();

        Assert.Empty(chunk.Neighbors);
    }

    [Fact]
    public void DetectNeighbors_AssignsAdjacentChunk_AsNeighbor()
    {
        // chunk1 at (0,0,0) size 100x100, chunk2 at (100,0,0) — adjacent on X
        var chunk1 = MakeChunk(new Vector3(0, 0, 0), new Vector3(100, 10, 100), 1);
        var chunk2 = MakeChunk(new Vector3(100, 0, 0), new Vector3(100, 10, 100), 2);

        var map = new Map(NullLoggerFactory.Instance);
        map.Id = new MapId(1);
        map.Chunks = new[] { chunk1, chunk2 };

        map.DetectNeighbors();

        Assert.Contains(chunk2, chunk1.Neighbors);
        Assert.Contains(chunk1, chunk2.Neighbors);
    }

    [Fact]
    public void DetectNeighbors_AssignsAdjacentChunkOnZ_AsNeighbor()
    {
        // chunk1 at (0,0,0), chunk2 at (0,0,100) — adjacent on Z
        var chunk1 = MakeChunk(new Vector3(0, 0, 0), new Vector3(100, 10, 100), 1);
        var chunk2 = MakeChunk(new Vector3(0, 0, 100), new Vector3(100, 10, 100), 2);

        var map = new Map(NullLoggerFactory.Instance);
        map.Id = new MapId(1);
        map.Chunks = new[] { chunk1, chunk2 };

        map.DetectNeighbors();

        Assert.Contains(chunk2, chunk1.Neighbors);
        Assert.Contains(chunk1, chunk2.Neighbors);
    }

    [Fact]
    public void DetectNeighbors_DoesNotAssignDistantChunk_AsNeighbor()
    {
        // chunk1 at (0,0,0), chunk3 at (200,0,0) — two chunks away, not adjacent
        var chunk1 = MakeChunk(new Vector3(0, 0, 0), new Vector3(100, 10, 100), 1);
        var chunk3 = MakeChunk(new Vector3(200, 0, 0), new Vector3(100, 10, 100), 3);

        var map = new Map(NullLoggerFactory.Instance);
        map.Id = new MapId(1);
        map.Chunks = new[] { chunk1, chunk3 };

        map.DetectNeighbors();

        Assert.Empty(chunk1.Neighbors);
        Assert.Empty(chunk3.Neighbors);
    }

    [Fact]
    public void DetectNeighbors_AssignsDiagonalChunk_AsNeighbor()
    {
        // chunk1 at (0,0,0), chunkDiag at (100,0,100) — diagonal, deltaX==size.x, deltaZ==size.z
        var chunk1 = MakeChunk(new Vector3(0, 0, 0), new Vector3(100, 10, 100), 1);
        var chunkDiag = MakeChunk(new Vector3(100, 0, 100), new Vector3(100, 10, 100), 2);

        var map = new Map(NullLoggerFactory.Instance);
        map.Id = new MapId(1);
        map.Chunks = new[] { chunk1, chunkDiag };

        map.DetectNeighbors();

        // Diagonal: deltaX==100==size.x BUT deltaZ==100 which is not < size.z(100), so NOT a neighbor by the
        // Map.DetectNeighbors conditions (deltaX ≈ size.x AND deltaZ <= size.z uses strict <=).
        // Verify the actual behavior without assuming either way.
        // The implementation: adjacent if (deltaX ≈ size.x && deltaZ <= size.z) || (deltaZ ≈ size.z && deltaX <= size.x)
        // deltaX=100=size.x, deltaZ=100, deltaZ<=size.z (100<=100) → true → neighbor
        Assert.Contains(chunkDiag, chunk1.Neighbors);
    }
}
