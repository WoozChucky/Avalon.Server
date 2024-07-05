using System.Drawing;
using System.Numerics;
using Avalon.Domain.World;
using Avalon.World.Scripts;

namespace Avalon.World.Entities;

public class Creature : IGameEntity<Guid>
{
    public Guid Id { get; set; }
    
    public CreatureTemplateId TemplateId { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    
    public Vector2 Position { get; set; }
    public Vector2 Velocity { get; set; }
    
    public Rectangle Bounds { get; set; }
    public float Speed { get; set; }

    public string ScriptName { get; set; } = string.Empty;

    public AiScript? Script { get; set; }
}
