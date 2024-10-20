using Avalon.Common.ValueObjects;
using Avalon.World.Public;
using Avalon.World.Public.Creatures;

namespace Avalon.World.Maps;

public class WorldGrid
{
    private readonly IList<Map> _maps = [];
    private readonly object _mapsLock = new();

    public IList<Map> Maps
    {
        get
        {
            lock (_mapsLock)
            {
                return _maps;
            }
        }
    }

    public void AddPlayer(IWorldConnection connection)
    {
        if (connection.Character == null)
        {
            throw new InvalidOperationException("Character not found in connection");
        }

        ushort mapId = connection.Character.Map;

        Map? map;
        lock (_mapsLock)
        {
            map = _maps.FirstOrDefault(m => m.Id == mapId);
        }

        if (map == null)
        {
            throw new InvalidOperationException($"Map {mapId} not found");
        }

        map.AddPlayer(connection);
    }

    public void RemovePlayer(IWorldConnection connection)
    {
        if (connection.Character == null)
        {
            throw new InvalidOperationException("Character not found in connection");
        }

        ushort mapId = connection.Character.Map;

        Map? map;
        lock (_mapsLock)
        {
            map = _maps.FirstOrDefault(m => m.Id == mapId);
        }

        if (map == null)
        {
            throw new InvalidOperationException($"Map {mapId} not found");
        }

        map.RemovePlayer(connection);
    }

    public void AddMap(Map map)
    {
        lock (_mapsLock)
        {
            _maps.Add(map);
        }
    }

    public void DetectNeighbors()
    {
        foreach (Map map in Maps)
        {
            map.DetectNeighbors();
        }
    }

    public void SpawnStartingEntities()
    {
        foreach (Map map in Maps)
        {
            map.SpawnStartingEntities();
        }
    }

    public void OnPlayerMoved(IWorldConnection connection)
    {
        if (connection.Character == null)
        {
            throw new InvalidOperationException("Character not found in connection");
        }

        Chunk? chunk = GetChunk(connection.Character.ChunkId);

        if (chunk == null)
        {
            throw new InvalidOperationException($"Invalid movement, chunk {connection.Character.ChunkId} not found");
        }

        chunk.OnPlayerMoved(connection);
    }

    public Chunk? GetChunk(ChunkId chunkId) => Maps.SelectMany(m => m.Chunks).FirstOrDefault(c => c.Id == chunkId);

    public ICreature? FindCreature(CreatureId creatureId, ChunkId chunkId)
    {
        Map? chunk = Maps.FirstOrDefault(m => m.Chunks.FirstOrDefault(c => c.Id == chunkId) != null);

        return chunk?.FindCreature(creatureId);
    }

    public ICreature? FindCreature(CreatureId creatureId)
    {
        IList<Map> maps = Maps;
        foreach (Map map in maps)
        {
            ICreature? creature = map.FindCreature(creatureId);
            if (creature != null)
            {
                return creature;
            }
        }

        return null;
    }
}
