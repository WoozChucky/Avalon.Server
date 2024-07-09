using System.Drawing;
using Avalon.Common.Mathematics;
using Avalon.Domain.World;
using Avalon.World.Scripts;

namespace Avalon.World.Entities;

public class Creature : IGameEntity<Guid>
{
    public Guid Id { get; set; }
    
    public CreatureTemplateId TemplateId { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public Vector3 Position
    {
        set
        {
            _position = value;
            Bounds = new Rectangle((int)value.x, (int)value.y, 1, 1);
        }
        get => _position;
    }
    
    private Vector3 _position;

    public Vector3 Velocity { get; set; }
    
    public Rectangle Bounds { get; set; }
    public float Speed { get; set; }

    public string ScriptName { get; set; } = string.Empty;

    public AiScript? Script { get; set; }

}
