using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;

namespace Avalon.World.Public.Maps;

public interface IChunk
{
    uint Id { get; }
    bool Enabled { get; set; }
    ChunkMetadata Metadata { get; }
    IChunkNavigator Navigator { get; }
    List<IChunk> Neighbors { get; set; }
    IReadOnlyDictionary<CharacterId, ICharacter> Characters { get; }
    IReadOnlyDictionary<CreatureId, ICreature> Creatures { get; }
    
    void AddCharacter(IWorldConnection connection);
    void RemoveCharacter(IWorldConnection connection);
    void RemoveCreature(ICreature creature);
    void BroadcastChunkStateTo(ICharacter character);
    void BroadcastAttackAnimation(CreatureId creatureId, ushort animationId);
    void BroadcastCreatureHit(CharacterId attackerId, CreatureId creatureId, uint currentHealth, uint damage);
    void BroadcastCreatureDeath(CreatureId creatureId);
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
