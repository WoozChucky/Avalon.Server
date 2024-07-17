using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Movement;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Maps;

namespace Avalon.World.Public.Creatures;

public interface ICreature : IGameEntity<Guid>
{
    public ICreatureMetadata Metadata { get; set; }
    public string Name { get; set; }
    public float Speed { get; set; }
    public uint Health { get; set; }
    public uint CurrentHealth { get; set; }
    public string ScriptName { get; set; }
    public AiScript? Script { get; set; }
    // public IChunk? Chunk { get; set; }
    MoveState MoveState { get; set; }

    void LookAt(Vector3 target);
    bool IsLookingAt(Vector3 target, float threshold = 0.1f);

    #region Events
    void OnHit(ICreature attacker, uint damage);
    void OnHit(ICharacter attacker, uint damage);
    #endregion
    
}
