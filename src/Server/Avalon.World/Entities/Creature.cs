using System.Drawing;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;

namespace Avalon.World.Entities;

public class Creature : ICreature
{
    public Guid Id { get; set; }
    
    public CreatureTemplateId TemplateId { get; set; } = null!;

    public ICreatureMetadata Metadata { get; set; }
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

    public Vector3 Orientation { get; set; }

    private Vector3 _position;

    public Vector3 Velocity { get; set; }
    
    public Rectangle Bounds { get; set; }
    public float Speed { get; set; }

    public string ScriptName { get; set; } = string.Empty;

    public AiScript? Script { get; set; }
    
    public void OnHit(ICreature attacker, uint damage)
    {
        throw new NotImplementedException();
    }

    public void OnHit(ICharacter attacker, uint damage)
    {
        throw new NotImplementedException();
    }
}
