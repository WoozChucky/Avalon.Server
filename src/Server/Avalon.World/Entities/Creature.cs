using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.State;
using Avalon.World.Public;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Spells;

namespace Avalon.World.Entities;

public class Creature : ICreature
{
    public ObjectGuid Guid { get; set; }

    public CreatureTemplateId TemplateId { get; set; } = null!;

    public ICreatureMetadata Metadata { get; set; }
    public string Name { get; set; } = string.Empty;
    public ushort Level { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Orientation { get; set; }
    public Vector3 Velocity { get; set; }
    public float Speed { get; set; }
    public uint Health { get; set; }
    public uint CurrentHealth { get; set; }
    public PowerType PowerType { get; set; }
    public uint? Power { get; set; }
    public uint? CurrentPower { get; set; }

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

    public void Died(IUnit killer)
    {
        OnCreatureKilled?.Invoke(this, killer);
    }

    public void OnHit(IUnit attacker, uint damage)
    {
        Script?.OnHit(attacker, damage);
    }

    public void SendAttackAnimation(ISpell? spell)
    {
        OnUnitAttackAnimation?.Invoke(this, spell);
    }

    public void SendFinishCastAnimation(ISpell spell)
    {
        OnUnitFinishedCastAnimation?.Invoke(this, spell);
    }

    public void SendInterruptedCastAnimation(ISpell spell)
    {
        OnUnitInterruptedCastAnimation?.Invoke(this, spell);
    }

    public static event CreatureKilledDelegate? OnCreatureKilled;
    public static event UnitAttackAnimationDelegate? OnUnitAttackAnimation;
    public static event UnitFinishedCastAnimationDelegate? OnUnitFinishedCastAnimation;
    public static event UnitInterruptedCastAnimationDelegate? OnUnitInterruptedCastAnimation;
}
