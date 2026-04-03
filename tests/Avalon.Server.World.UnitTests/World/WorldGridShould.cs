using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.World.Maps;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.World;

public class WorldGridShould
{
    private readonly WorldGrid _grid = new WorldGrid();

    private static Map MakeMap(ushort id)
    {
        var map = new Map(NullLoggerFactory.Instance);
        map.Id = new MapId(id);
        map.Chunks = Array.Empty<Chunk>();
        return map;
    }

    [Fact]
    public void BeEmpty_WhenCreated()
    {
        Assert.Empty(_grid.Maps);
    }

    [Fact]
    public void AddMap_AndFindItInMaps()
    {
        var map = MakeMap(1);

        _grid.AddMap(map);

        Assert.Single(_grid.Maps);
        Assert.Equal((MapId)1, _grid.Maps[0].Id);
    }

    [Fact]
    public void AddMultipleMaps()
    {
        _grid.AddMap(MakeMap(1));
        _grid.AddMap(MakeMap(2));

        Assert.Equal(2, _grid.Maps.Count);
    }

    [Fact]
    public void GetChunk_ReturnsNull_WhenNoMapsRegistered()
    {
        var result = _grid.GetChunk(new ChunkId(1u));

        Assert.Null(result);
    }

    [Fact]
    public void GetChunk_ReturnsNull_WhenChunkNotFound()
    {
        _grid.AddMap(MakeMap(1));

        var result = _grid.GetChunk(new ChunkId(999u));

        Assert.Null(result);
    }

    [Fact]
    public void AddPlayer_ThrowsInvalidOperationException_WhenCharacterIsNull()
    {
        var connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns((ICharacter?)null);

        Assert.Throws<InvalidOperationException>(() => _grid.AddPlayer(connection));
    }

    [Fact]
    public void AddPlayer_ThrowsInvalidOperationException_WhenMapNotRegistered()
    {
        var character = Substitute.For<ICharacter>();
        character.Map.Returns(new MapId(99));
        var connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);

        Assert.Throws<InvalidOperationException>(() => _grid.AddPlayer(connection));
    }

    [Fact]
    public void RemovePlayer_ThrowsInvalidOperationException_WhenCharacterIsNull()
    {
        var connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns((ICharacter?)null);

        Assert.Throws<InvalidOperationException>(() => _grid.RemovePlayer(connection));
    }

    [Fact]
    public void RemovePlayer_ThrowsInvalidOperationException_WhenMapNotRegistered()
    {
        var character = Substitute.For<ICharacter>();
        character.Map.Returns(new MapId(99));
        var connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);

        Assert.Throws<InvalidOperationException>(() => _grid.RemovePlayer(connection));
    }

    [Fact]
    public void OnPlayerMoved_ThrowsInvalidOperationException_WhenCharacterIsNull()
    {
        var connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns((ICharacter?)null);

        Assert.Throws<InvalidOperationException>(() => _grid.OnPlayerMoved(connection));
    }

    [Fact]
    public void FindCreature_ReturnsNull_WhenNoMapsRegistered()
    {
        var result = _grid.FindCreature(new CreatureId(1u));

        Assert.Null(result);
    }

    [Fact]
    public void FindCreature_WithChunkId_ReturnsNull_WhenChunkNotFound()
    {
        _grid.AddMap(MakeMap(1));

        var result = _grid.FindCreature(new CreatureId(1u), new ChunkId(999u));

        Assert.Null(result);
    }

    [Fact]
    public void DetectNeighbors_DoesNotThrow_WhenMapsHaveNoChunks()
    {
        _grid.AddMap(MakeMap(1));
        _grid.AddMap(MakeMap(2));

        var ex = Record.Exception(() => _grid.DetectNeighbors());

        Assert.Null(ex);
    }

    [Fact]
    public void SpawnStartingEntities_DoesNotThrow_WhenMapsHaveNoChunks()
    {
        _grid.AddMap(MakeMap(1));

        var ex = Record.Exception(() => _grid.SpawnStartingEntities());

        Assert.Null(ex);
    }

    [Fact]
    public void OnPlayerMoved_ThrowsInvalidOperationException_WhenChunkNotFound()
    {
        var character = Substitute.For<ICharacter>();
        character.ChunkId.Returns(new ChunkId(999u));
        var connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);

        _grid.AddMap(MakeMap(1)); // map exists but no chunks

        Assert.Throws<InvalidOperationException>(() => _grid.OnPlayerMoved(connection));
    }

    [Fact]
    public void FindCreature_ReturnsNull_WhenMapExistsButCreatureNotFound()
    {
        _grid.AddMap(MakeMap(1));

        var result = _grid.FindCreature(new CreatureId(999u));

        Assert.Null(result);
    }
}
