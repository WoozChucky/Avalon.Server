using Avalon.Common.Mathematics;

namespace Avalon.World.Maps.Virtualized;

[Serializable]
public class VirtualizedMap
{
    public ushort Id { get; set; }
    public Vector3 Size;
    public List<ChunkMetadata> Chunks;
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


public static class BinaryDeserializationHelper
{
    public static async Task<VirtualizedMap> ReadMapFromFile(string filePath, CancellationToken token)
    {
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(fs);
        
        // Read Map Id
        var id = reader.ReadUInt16();
        
        token.ThrowIfCancellationRequested();
        
        // Read Map Size
        var size = ReadVector3(reader);
        
        token.ThrowIfCancellationRequested();

        // Read number of Chunks
        var chunkCount = reader.ReadInt32();
        
        token.ThrowIfCancellationRequested();

        // Read each Chunk
        var chunks = new List<ChunkMetadata>();
        for (var i = 0; i < chunkCount; i++)
        {
            token.ThrowIfCancellationRequested();
            chunks.Add(ReadChunk(reader));
        }

        return new VirtualizedMap { Id = id, Size = size, Chunks = chunks };
    }

    private static ChunkMetadata ReadChunk(BinaryReader reader)
    {
        string name = reader.ReadString();
        Vector3 position = ReadVector3(reader);
        Vector3 size = ReadVector3(reader);

        // Read number of Trees
        int treeCount = reader.ReadInt32();

        // Read each TreeInfo
        List<TreeInfo> trees = new List<TreeInfo>();
        for (int i = 0; i < treeCount; i++)
        {
            trees.Add(ReadTreeInfo(reader));
        }
        
        // Read number of Creatures
        int creatureCount = reader.ReadInt32();
        List<CreatureInfo> creatures = new List<CreatureInfo>();
        for (int i = 0; i < creatureCount; i++)
        {
            creatures.Add(ReadCreatureInfo(reader));
        }
        
        // Read Heightmap
        int heightmapWidth = reader.ReadInt32();
        int heightmapHeight = reader.ReadInt32();
        float[,] heightmap = new float[heightmapWidth, heightmapHeight];
        for (int x = 0; x < heightmapWidth; x++)
        {
            for (int y = 0; y < heightmapHeight; y++)
            {
                heightmap[x, y] = reader.ReadSingle();
            }
        }
        
        // Read NavMesh
        int vertexCount = reader.ReadInt32();
        if (vertexCount == 0)
        {
            return new ChunkMetadata { Name = name, Position = position, Size = size, Trees = trees, Creatures = creatures, Heightmap = heightmap, NavMesh = null };
        }

        Vector3[] vertices = new Vector3[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            vertices[i] = ReadVector3(reader);
        }

        int indexCount = reader.ReadInt32();
        int[] indices = new int[indexCount];
        for (int i = 0; i < indexCount; i++)
        {
            indices[i] = reader.ReadInt32();
        }

        int areaCount = reader.ReadInt32();
        int[] areas = new int[areaCount];
        for (int i = 0; i < areaCount; i++)
        {
            areas[i] = reader.ReadInt32();
        }

        return new ChunkMetadata
        {
            Name = name, 
            Position = position, 
            Size = size, 
            Trees = trees,
            Creatures = creatures,
            Heightmap = heightmap, 
            NavMesh = new NavMeshInfo { Vertices = vertices, Indices = indices, Areas = areas }
        };
    }

    private static TreeInfo ReadTreeInfo(BinaryReader reader)
    {
        Vector3 position = ReadVector3(reader);
        Vector3 size = ReadVector3(reader);
        int prototypeIndex = reader.ReadInt32();
        return new TreeInfo { Position = position, Size = size, PrototypeIndex = prototypeIndex };
    }
    
    private static CreatureInfo ReadCreatureInfo(BinaryReader reader)
    {
        Vector3 position = ReadVector3(reader);
        ulong prototypeIndex = reader.ReadUInt64();
        return new CreatureInfo { Position = position, PrototypeIndex = prototypeIndex };
    }

    private static Vector3 ReadVector3(BinaryReader reader)
    {
        float x = reader.ReadSingle();
        float y = reader.ReadSingle();
        float z = reader.ReadSingle();
        return new Vector3(x, y, z);
    }
}
