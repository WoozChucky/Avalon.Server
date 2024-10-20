using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Units;

namespace Avalon.World.Public.Maps;

public interface IChunk
{
    uint Id { get; }
    bool Enabled { get; set; }
    ChunkMetadata Metadata { get; }
    IChunkNavigator Navigator { get; }
    List<IChunk> Neighbors { get; set; }
    IReadOnlyDictionary<ObjectGuid, ICharacter> Characters { get; }
    IReadOnlyDictionary<ObjectGuid, ICreature> Creatures { get; }

    void AddCharacter(IWorldConnection connection);
    void RemoveCharacter(IWorldConnection connection);
    void RemoveCreature(ICreature creature);
    void BroadcastChunkStateTo(ICharacter character);
    void BroadcastUnitHit(IUnit attacker, IUnit target, uint currentHealth, uint damage);
    void RespawnCreature(ICreature creature);
}

[Serializable]
public class ChunkMetadata
{
    public List<CreatureInfo> Creatures; // Creatures
    public string GeometryFile;
    public string MeshFile;
    public string Name;
    public Vector3 Position;
    public Vector3 Size;
    public List<TreeInfo> Trees; // Possibly collidable objects
}

[Serializable]
public class TreeInfo
{
    public Vector3 Position;
    public int PrototypeIndex;
    public Vector3 Size;
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
    public int[] Areas;
    public int[] Indices;
    public Vector3[] Vertices;
}
