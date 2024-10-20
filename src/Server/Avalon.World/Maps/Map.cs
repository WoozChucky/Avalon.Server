using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Public;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Maps;

public class Map(ILoggerFactory loggerFactory)
{
    private readonly ILogger<Map> _logger = loggerFactory.CreateLogger<Map>();
    public MapId Id { get; set; }
    public MapTemplate Metadata { get; set; }
    public Vector3 Size { get; set; }
    public IReadOnlyCollection<Chunk> Chunks { get; set; }

    public void AddPlayer(IWorldConnection connection)
    {
        if (connection.Character == null)
        {
            throw new InvalidOperationException("Character not found in connection");
        }

        Vector3 position = connection.Character.Position;

        IChunk? chunk = GetChunk(position);
        if (chunk == null)
        {
            throw new InvalidOperationException($"Chunk not found for position {position}");
        }

        chunk.AddCharacter(connection);
        _logger.LogInformation("Player {CharacterId} added to chunk {ChunkId} of map {MapId}",
            connection.Character.Name, chunk.Id, Id);
    }

    public void RemovePlayer(IWorldConnection connection)
    {
        if (connection.Character == null)
        {
            throw new InvalidOperationException("Character not found in connection");
        }

        Vector3 position = connection.Character.Position;

        IChunk? chunk = GetChunk(position);
        if (chunk == null)
        {
            throw new InvalidOperationException($"Chunk not found for position {position}");
        }

        chunk.RemoveCharacter(connection);
        _logger.LogInformation("Player {CharacterId} removed from chunk {ChunkId} of map {MapId}",
            connection.Character.Name, chunk.Id, Id);
    }

    public void DetectNeighbors()
    {
        foreach (Chunk chunk in Chunks)
        {
            // Find the neighbors of the chunk
            List<IChunk> neighbors = new List<IChunk>();
            foreach (Chunk otherChunk in Chunks)
            {
                if (chunk == otherChunk)
                {
                    continue;
                }

                chunk.Neighbors.Clear();

                // Calculate the distance between chunks in the X and Z directions
                float deltaX = Mathf.Abs(chunk.Metadata.Position.x - otherChunk.Metadata.Position.x);
                float deltaZ = Mathf.Abs(chunk.Metadata.Position.z - otherChunk.Metadata.Position.z);

                // Check if the chunks are adjacent (including diagonally)
                if (Mathf.Approximately(deltaX, chunk.Metadata.Size.x) && deltaZ <= chunk.Metadata.Size.z)
                {
                    neighbors.Add(otherChunk);
                }
                else if (Mathf.Approximately(deltaZ, chunk.Metadata.Size.z) && deltaX <= chunk.Metadata.Size.x)
                {
                    neighbors.Add(otherChunk);
                }
            }

            chunk.Neighbors = neighbors;
        }
    }

    private IChunk? GetChunk(Vector3 position)
    {
        foreach (Chunk chunk in Chunks)
        {
            // Calculate the chunk bounds
            Vector3 min = chunk.Metadata.Position;
            Vector3 max = chunk.Metadata.Position + new Vector3(chunk.Metadata.Size.x, 0, chunk.Metadata.Size.z);
            // Check if the position is within the bounds of this chunk
            if (position.x >= min.x && position.x < max.x &&
                position.z >= min.z && position.z < max.z)
            {
                return chunk;
            }
        }

        return null;
    }

    public void SpawnStartingEntities()
    {
        foreach (Chunk chunk in Chunks)
        {
            chunk.SpawnStartingEntities();
        }
    }

    public ICreature? FindCreature(CreatureId creatureId)
    {
        foreach (Chunk chunk in Chunks)
        {
            IEnumerable<ICreature> creatures = chunk.Creatures.Values;
            foreach (ICreature creature in creatures)
            {
                if (creature.Guid.Id == creatureId)
                {
                    return creature;
                }
            }
        }

        return null;
    }
}
