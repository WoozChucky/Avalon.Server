using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;
using Avalon.World.Public.Characters;

namespace Avalon.World.Public.Creatures;

public delegate void CreatureKilledDelegate(ICreature creature, IGameEntity killer);

public interface ICreature : IGameEntity
{
    ICreatureMetadata Metadata { get; set; }
    string Name { get; set; }
    float Speed { get; set; }
    string ScriptName { get; set; }
    AiScript? Script { get; set; }

    void LookAt(Vector3 target);
    bool IsLookingAt(Vector3 target, float threshold = 0.1f);
    void Died(IGameEntity killer);
    
    #region Events
    void OnHit(ICreature attacker, uint damage);
    void OnHit(ICharacter attacker, uint damage);
    #endregion

}
