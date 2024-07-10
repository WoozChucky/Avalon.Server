using Avalon.Common.Mathematics;
using Avalon.World.Public.Creatures;

namespace Avalon.World.Public.Maps;

public interface IChunk
{
    public uint Id { get; }
    public bool Enabled { get; set; }
    public ChunkMetadata Metadata { get; init; }
    public List<IChunk> Neighbors { get; set; }

    IReadOnlyList<IWorldConnection> GetConnections();
    IReadOnlyList<ICreature> GetCreatures();
}

[Serializable]
public class ChunkMetadata
{
    public string Name;
    public Vector3 Position;
    public Vector3 Size;
    public List<TreeInfo> Trees; // Possibly collidable objects
    public List<CreatureInfo> Creatures; // Creatures
    public float[,] Heightmap; // Heightmap data
    public NavMeshInfo NavMesh;
}

[Serializable]
public class TreeInfo
{
    public Vector3 Position;
    public Vector3 Size;
    public int PrototypeIndex;
}

[Serializable]
public class CreatureInfo
{
    public Vector3 Position;
    public ulong PrototypeIndex;
}

[Serializable]
public class NavMeshInfo
{
    public Vector3[] Vertices;
    public int[] Indices;
    public int[] Areas;
}
