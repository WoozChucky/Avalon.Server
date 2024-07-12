using Avalon.World.Public.Characters;

namespace Avalon.World.Public.Creatures;

public interface ICreature : IGameEntity<Guid>
{
    public ICreatureMetadata Metadata { get; set; }
    public string Name { get; set; }
    public float Speed { get; set; }
    public string ScriptName { get; set; }
    public AiScript? Script { get; set; }
    
    void OnHit(ICreature attacker, uint damage);
    void OnHit(ICharacter attacker, uint damage);
}
