using Avalon.Common.Mathematics;
using Avalon.World.Public.Maps;

namespace Avalon.World.Maps.Virtualized;

[Serializable]
public class VirtualizedMap
{
    public ushort Id { get; set; }
    public Vector3 Size;
    public List<ChunkMetadata> Chunks;
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
        
        // Read Geometry file name
        var geometryFile = reader.ReadString();
        
        // Read Mesh file name
        var meshFile = reader.ReadString();

        return new ChunkMetadata
        {
            Name = name, 
            Position = position, 
            Size = size, 
            Trees = trees,
            Creatures = creatures,
            GeometryFile = geometryFile,
            MeshFile = meshFile,
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
