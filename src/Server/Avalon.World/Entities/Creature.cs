using System.Drawing;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Movement;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;

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
    public uint Health { get; set; }
    public uint CurrentHealth { get; set; }

    public string ScriptName { get; set; } = string.Empty;

    public AiScript? Script { get; set; }
    public MoveState MoveState { get; set; } = MoveState.Idle;

    public void LookAt(Vector3 target)
    {
        var direction = Vector3.Normalize(target - Position);
        var yawRadians = Mathf.Atan2(direction.x, direction.z);
        var yawDegrees = yawRadians * Mathf.Rad2Deg;
        Orientation = new Vector3(0.0f, yawDegrees, 0.0f);
    }
    
    public bool IsLookingAt(Vector3 target, float threshold = 0.1f)
    {
        var direction = Vector3.Normalize(target - Position);
        var yawRadians = Mathf.Atan2(direction.x, direction.z);
        var yawDegrees = yawRadians * Mathf.Rad2Deg;
        var orientation = new Vector3(0.0f, yawDegrees, 0.0f);
        return Mathf.Abs(Orientation.y - orientation.y) < threshold;
    }

    public void OnHit(ICreature attacker, uint damage)
    {
        Script?.OnHit(attacker, damage);
    }

    public void OnHit(ICharacter attacker, uint damage)
    {
        Script?.OnHit(attacker, damage);
    }
}
