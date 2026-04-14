using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.State;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Spells;
using Avalon.World.Public.Units;

namespace Avalon.World.Entities;

public class Creature : ICreature
{
    // Initialize to GameEntityFields.None (value 1, not 0) so the first ConsumeDirtyFields()
    // returns None and correctly skips the _frameDirtyFields insertion check.
    private GameEntityFields _dirtyFields = GameEntityFields.None;

    private Vector3 _position;
    private Vector3 _orientation;
    private Vector3 _velocity;
    private uint _health;
    private uint _currentHealth;
    private PowerType _powerType;
    private uint? _power;
    private uint? _currentPower;
    private ushort _level;
    private MoveState _moveState = MoveState.Idle;

    public CreatureTemplateId TemplateId { get; set; } = null!;
    public ObjectGuid Guid { get; set; }
    public ICreatureMetadata Metadata { get; set; }
    public string Name { get; set; } = string.Empty;
    public float Speed { get; set; }
    public string ScriptName { get; set; } = string.Empty;
    public AiScript? Script { get; set; }

    public ushort Level
    {
        get => _level;
        set { _level = value; _dirtyFields |= GameEntityFields.Level; }
    }

    public Vector3 Position
    {
        get => _position;
        set { _position = value; _dirtyFields |= GameEntityFields.Position; }
    }

    public Vector3 Orientation
    {
        get => _orientation;
        set { _orientation = value; _dirtyFields |= GameEntityFields.Orientation; }
    }

    public Vector3 Velocity
    {
        get => _velocity;
        set { _velocity = value; _dirtyFields |= GameEntityFields.Velocity; }
    }

    public uint Health
    {
        get => _health;
        set { _health = value; _dirtyFields |= GameEntityFields.Health; }
    }

    public uint CurrentHealth
    {
        get => _currentHealth;
        set { _currentHealth = value; _dirtyFields |= GameEntityFields.CurrentHealth; }
    }

    public PowerType PowerType
    {
        get => _powerType;
        set { _powerType = value; _dirtyFields |= GameEntityFields.PowerType; }
    }

    public uint? Power
    {
        get => _power;
        set { _power = value; _dirtyFields |= GameEntityFields.Power; }
    }

    public uint? CurrentPower
    {
        get => _currentPower;
        set { _currentPower = value; _dirtyFields |= GameEntityFields.CurrentPower; }
    }

    public MoveState MoveState
    {
        get => _moveState;
        set { _moveState = value; _dirtyFields |= GameEntityFields.MoveState; }
    }

    public GameEntityFields ConsumeDirtyFields()
    {
        var dirty = _dirtyFields;
        _dirtyFields = GameEntityFields.None;
        return dirty;
    }

    public void LookAt(Vector3 target)
    {
        Vector3 direction = Vector3.Normalize(target - Position);
        float yawRadians = Mathf.Atan2(direction.x, direction.z);
        float yawDegrees = yawRadians * Mathf.Rad2Deg;
        Orientation = new Vector3(0.0f, yawDegrees, 0.0f);
    }

    public bool IsLookingAt(Vector3 target, float threshold = 0.1f)
    {
        Vector3 direction = Vector3.Normalize(target - Position);
        float yawRadians = Mathf.Atan2(direction.x, direction.z);
        float yawDegrees = yawRadians * Mathf.Rad2Deg;
        Vector3 orientation = new(0.0f, yawDegrees, 0.0f);
        return Mathf.Abs(Orientation.y - orientation.y) < threshold;
    }

    public void Died(IUnit killer) => OnCreatureKilled?.Invoke(this, killer);

    public void OnHit(IUnit attacker, uint damage) => Script?.OnHit(attacker, damage);

    public void SendAttackAnimation(ISpell? spell) => OnUnitAttackAnimation?.Invoke(this, spell);

    public void SendFinishCastAnimation(ISpell spell) => OnUnitFinishedCastAnimation?.Invoke(this, spell);

    public void SendInterruptedCastAnimation(ISpell spell) => OnUnitInterruptedCastAnimation?.Invoke(this, spell);

    public static event CreatureKilledDelegate? OnCreatureKilled;
    public static event UnitAttackAnimationDelegate? OnUnitAttackAnimation;
    public static event UnitFinishedCastAnimationDelegate? OnUnitFinishedCastAnimation;
    public static event UnitInterruptedCastAnimationDelegate? OnUnitInterruptedCastAnimation;
}
