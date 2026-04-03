using Avalon.Common;
using Avalon.Common.Cryptography;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.World;
using Avalon.World.Maps;
using Avalon.World.Pools;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;
using Avalon.World.Public.Units;
using Avalon.World.Scripts;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Maps;

public class ChunkShould
{
    private readonly IPoolManager _poolManager = Substitute.For<IPoolManager>();
    private readonly IWorld _world = Substitute.For<IWorld>();
    private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();
    private readonly Chunk _chunk;

    private static readonly ChunkMetadata DefaultMetadata = new ChunkMetadata
    {
        Name = "TestChunk",
        Position = new Vector3(0, 0, 0),
        Size = new Vector3(100, 10, 100),
        Creatures = new List<CreatureInfo>(),
        Trees = new List<TreeInfo>(),
        MeshFile = "",
        GeometryFile = ""
    };

    public ChunkShould()
    {
        var scriptManager = Substitute.For<IScriptManager>();
        _serviceProvider.GetService(typeof(IScriptManager)).Returns(scriptManager);

        _chunk = new Chunk(
            NullLoggerFactory.Instance,
            _serviceProvider,
            mapId: 1,
            position: Vector2.zero,
            _poolManager,
            _world)
        {
            Metadata = DefaultMetadata,
            Id = new ChunkId(1u)
        };
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    private static IWorldConnection MakeConnection(ObjectGuid? guid = null)
    {
        var cryptoSession = Substitute.For<IAvalonCryptoSession>();
        cryptoSession.Encrypt(Arg.Any<byte[]>()).Returns(callInfo => callInfo.ArgAt<byte[]>(0));

        var connection = Substitute.For<IWorldConnection>();
        connection.CryptoSession.Returns(cryptoSession);

        var character = Substitute.For<ICharacter>();
        character.Guid.Returns(guid ?? new ObjectGuid(ObjectType.Character, 1u));
        character.Map.Returns(new MapId(1));
        character.Connection.Returns(connection);

        connection.Character.Returns(character);

        return connection;
    }

    private static ICreature MakeCreature(uint id = 1)
    {
        var creature = Substitute.For<ICreature>();
        creature.Guid.Returns(new ObjectGuid(ObjectType.Creature, id));
        return creature;
    }

    // ──────────────────────────────────────────────
    // Initial state
    // ──────────────────────────────────────────────

    [Fact]
    public void BeDisabled_WhenCreated()
    {
        Assert.False(_chunk.Enabled);
    }

    [Fact]
    public void HaveEmptyCharacters_WhenCreated()
    {
        Assert.Empty(_chunk.Characters);
    }

    [Fact]
    public void HaveEmptyCreatures_WhenCreated()
    {
        Assert.Empty(_chunk.Creatures);
    }

    // ──────────────────────────────────────────────
    // AddCharacter
    // ──────────────────────────────────────────────

    [Fact]
    public void BecomeEnabled_WhenFirstCharacterAdded()
    {
        var conn = MakeConnection();
        _chunk.AddCharacter(conn);

        Assert.True(_chunk.Enabled);
    }

    [Fact]
    public void StoreCharacter_WhenAdded()
    {
        var guid = new ObjectGuid(ObjectType.Character, 42u);
        var conn = MakeConnection(guid);

        _chunk.AddCharacter(conn);

        Assert.Single(_chunk.Characters);
        Assert.True(_chunk.Characters.ContainsKey(guid));
    }

    [Fact]
    public void SetChunkIdOnCharacter_WhenAdded()
    {
        var conn = MakeConnection();
        _chunk.AddCharacter(conn);

        conn.Character!.Received().ChunkId = _chunk.Id;
    }

    [Fact]
    public void EnableNeighbors_WhenCharacterAdded()
    {
        var neighbor = Substitute.For<IChunk>();
        _chunk.Neighbors.Add(neighbor);

        _chunk.AddCharacter(MakeConnection());

        neighbor.Received().Enabled = true;
    }

    [Fact]
    public void StoreMultipleCharacters()
    {
        _chunk.AddCharacter(MakeConnection(new ObjectGuid(ObjectType.Character, 1u)));
        _chunk.AddCharacter(MakeConnection(new ObjectGuid(ObjectType.Character, 2u)));

        Assert.Equal(2, _chunk.Characters.Count);
    }

    // ──────────────────────────────────────────────
    // RemoveCharacter
    // ──────────────────────────────────────────────

    [Fact]
    public void RemoveCharacter_RemovesItFromDictionary()
    {
        var guid = new ObjectGuid(ObjectType.Character, 5u);
        var conn = MakeConnection(guid);
        _chunk.AddCharacter(conn);

        _chunk.RemoveCharacter(conn);

        Assert.Empty(_chunk.Characters);
    }

    [Fact]
    public void BecomeDisabled_WhenLastCharacterRemoved()
    {
        var conn = MakeConnection();
        _chunk.AddCharacter(conn);
        _chunk.RemoveCharacter(conn);

        Assert.False(_chunk.Enabled);
    }

    [Fact]
    public void StayEnabled_WhenOneOfMultipleCharactersRemoved()
    {
        var conn1 = MakeConnection(new ObjectGuid(ObjectType.Character, 1u));
        var conn2 = MakeConnection(new ObjectGuid(ObjectType.Character, 2u));
        _chunk.AddCharacter(conn1);
        _chunk.AddCharacter(conn2);

        _chunk.RemoveCharacter(conn1);

        Assert.True(_chunk.Enabled);
    }

    [Fact]
    public void CallOnDisconnected_WhenCharacterRemoved()
    {
        var conn = MakeConnection();
        _chunk.AddCharacter(conn);

        _chunk.RemoveCharacter(conn);

        conn.Character!.Received(1).OnDisconnected();
    }

    [Fact]
    public void ResetChunkIdToZero_WhenCharacterRemoved()
    {
        var conn = MakeConnection();
        _chunk.AddCharacter(conn);

        _chunk.RemoveCharacter(conn);

        conn.Character!.Received().ChunkId = new ChunkId(0u);
    }

    [Fact]
    public void DisableEmptyNeighbors_WhenLastCharacterRemoved()
    {
        var neighbor = Substitute.For<IChunk>();
        neighbor.Characters.Returns(new Dictionary<ObjectGuid, ICharacter>());
        _chunk.Neighbors.Add(neighbor);

        var conn = MakeConnection();
        _chunk.AddCharacter(conn);
        _chunk.RemoveCharacter(conn);

        neighbor.Received().Enabled = false;
    }

    [Fact]
    public void NotDisableNeighborWithActiveCharacters_WhenChunkBecomesEmpty()
    {
        var occupiedNeighbor = Substitute.For<IChunk>();
        var neighborCharacters = new Dictionary<ObjectGuid, ICharacter>
        {
            [new ObjectGuid(ObjectType.Character, 99u)] = Substitute.For<ICharacter>()
        };
        occupiedNeighbor.Characters.Returns(neighborCharacters);
        _chunk.Neighbors.Add(occupiedNeighbor);

        var conn = MakeConnection();
        _chunk.AddCharacter(conn);
        _chunk.RemoveCharacter(conn);

        occupiedNeighbor.DidNotReceive().Enabled = false;
    }

    // ──────────────────────────────────────────────
    // AddCreature / RemoveCreature
    // ──────────────────────────────────────────────

    [Fact]
    public void StoreCreature_WhenAdded()
    {
        var creature = MakeCreature(10u);
        _chunk.AddCreature(creature);

        Assert.Single(_chunk.Creatures);
        Assert.True(_chunk.Creatures.ContainsKey(creature.Guid));
    }

    [Fact]
    public void RemoveCreature_ByInstance()
    {
        var creature = MakeCreature(11u);
        _chunk.AddCreature(creature);

        _chunk.RemoveCreature(creature);

        Assert.Empty(_chunk.Creatures);
    }

    [Fact]
    public void RemoveCreature_ByGuid()
    {
        var creature = MakeCreature(12u);
        _chunk.AddCreature(creature);

        _chunk.RemoveCreature(creature.Guid);

        Assert.Empty(_chunk.Creatures);
    }

    [Fact]
    public void RemoveCreature_DoesNotThrow_WhenGuidNotPresent()
    {
        var ex = Record.Exception(() => _chunk.RemoveCreature(new ObjectGuid(ObjectType.Creature, 999u)));
        Assert.Null(ex);
    }

    [Fact]
    public void StoreMultipleCreatures()
    {
        _chunk.AddCreature(MakeCreature(1u));
        _chunk.AddCreature(MakeCreature(2u));
        _chunk.AddCreature(MakeCreature(3u));

        Assert.Equal(3, _chunk.Creatures.Count);
    }

    // ──────────────────────────────────────────────
    // SpawnStartingEntities / RespawnCreature
    // ──────────────────────────────────────────────

    [Fact]
    public void SpawnStartingEntities_DelegatesToPoolManager()
    {
        _chunk.SpawnStartingEntities();

        _poolManager.Received(1).SpawnStartingEntities(_chunk);
    }

    [Fact]
    public void RespawnCreature_DelegatesToPoolManager()
    {
        var creature = MakeCreature();

        _chunk.RespawnCreature(creature);

        _poolManager.Received(1).SpawnEntity(_chunk, creature);
    }

    // ──────────────────────────────────────────────
    // BroadcastUnitHit
    // ──────────────────────────────────────────────

    [Fact]
    public void BroadcastUnitHit_SendsToAllCharactersInChunk()
    {
        var conn1 = MakeConnection(new ObjectGuid(ObjectType.Character, 1u));
        var conn2 = MakeConnection(new ObjectGuid(ObjectType.Character, 2u));
        _chunk.AddCharacter(conn1);
        _chunk.AddCharacter(conn2);

        var attacker = Substitute.For<IUnit>();
        var target = Substitute.For<IUnit>();
        attacker.Guid.Returns(new ObjectGuid(ObjectType.Character, 99u));
        target.Guid.Returns(new ObjectGuid(ObjectType.Character, 98u));

        _chunk.BroadcastUnitHit(attacker, target, 500u, 50u);

        // BroadcastUnitHit iterates _characters.Values and calls character.Connection.Send(...)
        conn1.Received(1).Send(Arg.Any<NetworkPacket>());
        conn2.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void BroadcastUnitHit_DoesNotThrow_WhenNoCharacters()
    {
        var attacker = Substitute.For<IUnit>();
        var target = Substitute.For<IUnit>();
        attacker.Guid.Returns(new ObjectGuid(ObjectType.Character, 99u));
        target.Guid.Returns(new ObjectGuid(ObjectType.Character, 98u));

        var ex = Record.Exception(() => _chunk.BroadcastUnitHit(attacker, target, 500u, 25u));

        Assert.Null(ex);
    }

    // ──────────────────────────────────────────────
    // OnPlayerMoved — IsNearChunkBorder / IsWithinChunk
    // ──────────────────────────────────────────────

    [Fact]
    public void OnPlayerMoved_DoesNotThrow_WhenCharacterFarFromBorder()
    {
        // Center of the 100x100 chunk — not near any border (threshold=10)
        var conn = MakeConnection();
        conn.Character!.Position.Returns(new Vector3(50, 0, 50));
        _chunk.AddCharacter(conn);

        var ex = Record.Exception(() => _chunk.OnPlayerMoved(conn));

        Assert.Null(ex);
    }

    [Fact]
    public void OnPlayerMoved_DoesNotThrow_WhenCharacterNearBorder_NoNeighbors()
    {
        // Near left border (x < 0+10 = 10)
        var conn = MakeConnection();
        conn.Character!.Position.Returns(new Vector3(5, 0, 50));
        _chunk.AddCharacter(conn);

        var ex = Record.Exception(() => _chunk.OnPlayerMoved(conn));

        Assert.Null(ex);
    }

    [Fact]
    public void OnPlayerMoved_MovesCharacterToNeighborChunk_WhenInsideNeighborBounds()
    {
        var neighborMetadata = new ChunkMetadata
        {
            Name = "Neighbor",
            Position = new Vector3(100, 0, 0),
            Size = new Vector3(100, 10, 100),
            Creatures = new List<CreatureInfo>(),
            Trees = new List<TreeInfo>(),
            MeshFile = "",
            GeometryFile = ""
        };
        var neighbor = Substitute.For<IChunk>();
        neighbor.Metadata.Returns(neighborMetadata);
        neighbor.Id.Returns(new ChunkId(2u));
        // RemoveCharacter will check neighbor.Characters.Count when _chunk becomes empty
        neighbor.Characters.Returns(new Dictionary<ObjectGuid, ICharacter>());
        _chunk.Neighbors.Add(neighbor);

        // Character is at x=110, inside the neighbor chunk (x: 100..200, z: 0..100)
        var conn = MakeConnection();
        conn.Character!.Position.Returns(new Vector3(110, 0, 50));
        _chunk.AddCharacter(conn);

        _chunk.OnPlayerMoved(conn);

        neighbor.Received(1).AddCharacter(conn);
        Assert.Empty(_chunk.Characters);
    }

    [Fact]
    public void OnPlayerMoved_DoesNotChangeChunk_WhenPositionIsWithinCurrentChunk()
    {
        var neighborMetadata = new ChunkMetadata
        {
            Name = "Neighbor",
            Position = new Vector3(100, 0, 0),
            Size = new Vector3(100, 10, 100),
            Creatures = new List<CreatureInfo>(),
            Trees = new List<TreeInfo>(),
            MeshFile = "",
            GeometryFile = ""
        };
        var neighbor = Substitute.For<IChunk>();
        neighbor.Metadata.Returns(neighborMetadata);
        neighbor.Id.Returns(new ChunkId(2u));
        _chunk.Neighbors.Add(neighbor);

        // Character is at center of current chunk — not inside neighbor's bounds
        var conn = MakeConnection();
        conn.Character!.Position.Returns(new Vector3(50, 0, 50));
        _chunk.AddCharacter(conn);

        _chunk.OnPlayerMoved(conn);

        neighbor.DidNotReceive().AddCharacter(Arg.Any<IWorldConnection>());
        Assert.Single(_chunk.Characters);
    }

    [Fact]
    public void OnPlayerMoved_BroadcastsNeighborState_WhenNearRightBorder()
    {
        var neighborMetadata = new ChunkMetadata
        {
            Name = "RightNeighbor",
            Position = new Vector3(100, 0, 0),
            Size = new Vector3(100, 10, 100),
            Creatures = new List<CreatureInfo>(),
            Trees = new List<TreeInfo>(),
            MeshFile = "",
            GeometryFile = ""
        };
        var neighbor = Substitute.For<IChunk>();
        neighbor.Metadata.Returns(neighborMetadata);
        neighbor.Id.Returns(new ChunkId(2u));
        _chunk.Neighbors.Add(neighbor);

        // character at x=95 → near right border (95 > 100-10=90), but still inside current chunk
        var conn = MakeConnection();
        conn.Character!.Position.Returns(new Vector3(95, 0, 50));
        _chunk.AddCharacter(conn);

        _chunk.OnPlayerMoved(conn);

        // GetNearbyChunks: nearRightBorder && neighborMinX ≈ currentMaxX (100 ≈ 100) → neighbor included
        neighbor.Received(1).BroadcastChunkStateTo(conn.Character);
    }
}
