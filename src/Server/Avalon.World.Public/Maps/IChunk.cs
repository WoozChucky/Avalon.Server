using Avalon.Common.Mathematics;
using Avalon.World.Public.Creatures;

namespace Avalon.World.Public.Maps;

public interface IChunk
{
    public uint Id { get; }
    public bool Enabled { get; set; }
    public ChunkMetadata Metadata { get; }
    public IChunkNavigator Navigator { get; }
    public List<IChunk> Neighbors { get; set; }

    public Task InitializeAsync();
    
    IReadOnlyList<IWorldConnection> GetConnections();
    IReadOnlyList<ICreature> GetCreatures();
    
    void AddPlayer(IWorldConnection connection);
    void RemovePlayer(IWorldConnection connection);
    void SendState(IWorldConnection connection);
}

[Serializable]
public class ChunkMetadata
{
    public string Name;
    public Vector3 Position;
    public Vector3 Size;
    public List<TreeInfo> Trees; // Possibly collidable objects
    public List<CreatureInfo> Creatures; // Creatures
    public string GeometryFile;
    public string MeshFile;
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
