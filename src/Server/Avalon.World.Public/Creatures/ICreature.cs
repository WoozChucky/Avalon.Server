using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;
using Avalon.World.Public.Characters;

namespace Avalon.World.Public.Creatures;

public interface ICreature : IGameEntity
{
    public ICreatureMetadata Metadata { get; set; }
    public string Name { get; set; }
    public float Speed { get; set; }
    public string ScriptName { get; set; }
    public AiScript? Script { get; set; }
    MoveState MoveState { get; set; }

    void LookAt(Vector3 target);
    bool IsLookingAt(Vector3 target, float threshold = 0.1f);

    #region Events
    void OnHit(ICreature attacker, uint damage);
    void OnHit(ICharacter attacker, uint damage);
    #endregion
    
}
