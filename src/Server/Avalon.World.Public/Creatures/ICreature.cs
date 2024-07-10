namespace Avalon.World.Public.Creatures;

public interface ICreature : IGameEntity<Guid>
{
    public ICreatureMetadata Metadata { get; set; }
    public string Name { get; set; }
    public float Speed { get; set; }
    public string ScriptName { get; set; }
    public AiScript? Script { get; set; }
}
