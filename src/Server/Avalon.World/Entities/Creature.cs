using System.Drawing;
using System.Numerics;
using Avalon.Common.Extensions;
using Avalon.Domain.World;
using Avalon.World.Scripts;

namespace Avalon.World.Entities;

public class Creature : IGameEntity<Guid>
{
    public Guid Id { get; set; }
    
    public CreatureTemplateId TemplateId { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public Vector2 Position
    {
        set
        {
            _position = value;
            Bounds = new Rectangle(value.ToPoint(), Bounds.Size);
        }
        get => _position;
    }
    
    private Vector2 _position;

    public Vector2 Velocity { get; set; }
    
    public Rectangle Bounds { get; set; }
    public float Speed { get; set; }

    public string ScriptName { get; set; } = string.Empty;

    public AiScript? Script { get; set; }

}
