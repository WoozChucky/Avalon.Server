using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.World.Configuration;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Spells;
using Avalon.World.Public.Units;
using Avalon.World.Spells;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Entities;

public class CharacterEntity : ICharacter
{
    private readonly ICharacterInventory _bag;
    private readonly ICharacterInventory _bank;
    private readonly ICharacterInventory _equipment;

    private readonly ILogger<CharacterEntity> _logger;
    private readonly RegenConfiguration _regenConfig;

    // Dirty-field tracking (matches Creature pattern; initialize to None so first consume is a no-op)
    private GameEntityFields _dirtyFields = GameEntityFields.None;

    // Power regen cast-suppression (5-second rule)
    private DateTime _lastCastTime = DateTime.MinValue;

    // Combat state
    private DateTime _lastCombatTime = DateTime.MinValue;

    public CharacterEntity()
    {
        _logger = null!;
        _equipment = null!;
        _bag = null!;
        _bank = null!;
        Spells = null!;
        _regenConfig = new RegenConfiguration();
    }

    public CharacterEntity(ILoggerFactory loggerFactory, Character character,
        RegenConfiguration regenConfig)
    {
        _logger = loggerFactory.CreateLogger<CharacterEntity>();
        Data = character;
        _equipment = new CharacterInventoryContainer(loggerFactory, InventoryType.Equipment);
        _bag = new CharacterInventoryContainer(loggerFactory, InventoryType.Bag);
        _bank = new CharacterInventoryContainer(loggerFactory, InventoryType.Bank);
        Spells = new CharacterSpellContainer(loggerFactory);
        CharacterGameState = new CharacterCharacterGameState();
        Guid = new ObjectGuid(ObjectType.Character, character.Id);
        _regenConfig = regenConfig;
        // Compute initial MovementSpeed from base + equipment + buff modifiers.
        // Without this MovementSpeed stays at the float default (0) and pins the player.
        CalculateMovementSpeed();
    }

    public Character? Data { get; init; }

    private float MovementSpeed { get; set; }

    public DateTime EnteredWorld { get; set; }

    public uint Stamina { get; set; }
    public uint RegenStat { get; set; }

    public bool IsInCombat =>
        _lastCombatTime != DateTime.MinValue &&
        (DateTime.UtcNow - _lastCombatTime).TotalSeconds < _regenConfig.CombatLeaveDelaySeconds;

    public ICharacterGameState CharacterGameState { get; }

    public ICharacterInventory this[InventoryType type] => type switch
    {
        InventoryType.Equipment => _equipment,
        InventoryType.Bag => _bag,
        InventoryType.Bank => _bank,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    public ICharacterSpells Spells { get; }

    public ObjectGuid Guid { get; set; }

    // Backing fields for dirty-tracked properties
    private uint _currentHealth;
    private uint? _currentPower;
    private PowerType _powerType;
    private MoveState _moveState = MoveState.Idle;
    private Vector3 _velocity;
    private ulong _requiredExperience;

    public uint Health
    {
        get => (uint)(Data?.Health ?? 0);
        set
        {
            if (Data != null)
            {
                Data.Health = (int)value;
                _dirtyFields |= GameEntityFields.Health;
            }
        }
    }

    public uint CurrentHealth
    {
        get => _currentHealth;
        set
        {
            _currentHealth = value;
            _dirtyFields |= GameEntityFields.CurrentHealth;
        }
    }

    public PowerType PowerType
    {
        get => _powerType;
        set
        {
            _powerType = value;
            _dirtyFields |= GameEntityFields.PowerType;
        }
    }

    public uint? Power
    {
        get => (uint)(Data?.Power1 ?? 0);
        set
        {
            if (Data != null)
            {
                Data.Power1 = (int)value!;
                _dirtyFields |= GameEntityFields.Power;
            }
        }
    }

    public uint? CurrentPower
    {
        get => _currentPower;
        set
        {
            _currentPower = value;
            _dirtyFields |= GameEntityFields.CurrentPower;
        }
    }

    public MoveState MoveState
    {
        get => _moveState;
        set
        {
            _moveState = value;
            _dirtyFields |= GameEntityFields.MoveState;
        }
    }

    public GameEntityFields ConsumeDirtyFields()
    {
        var dirty = _dirtyFields;
        _dirtyFields = GameEntityFields.None;
        return dirty;
    }

    public void MarkCombat() => _lastCombatTime = DateTime.UtcNow;

    public void OnHit(IUnit attacker, uint damage)
    {
        _logger.LogInformation("{Name} has been hit by unit {Attacker} for {Damage} damage", Name, attacker.Guid,
            damage);
        MarkCombat();
        CurrentHealth -= damage;
        if (CurrentHealth <= 0)
        {
            _logger.LogInformation("{Name} has died", Name);
            CurrentHealth = Health; // reset health while developing
        }

        // Send to self (routed via MapInstance which holds the connection)
        OnSelfDamaged?.Invoke(this, attacker, damage);
        // Broadcast to instance
        OnUnitDamaged?.Invoke(this, attacker, damage);
    }

    public void SendAttackAnimation(ISpell? spell) => OnUnitAttackAnimation?.Invoke(this, spell);

    public void SendFinishCastAnimation(ISpell spell) => OnUnitFinishedCastAnimation?.Invoke(this, spell);

    public void SendInterruptedCastAnimation(ISpell spell) => OnUnitInterruptedCastAnimation?.Invoke(this, spell);

    public Vector3 Position
    {
        get => new(Data?.X ?? 0, Data?.Y ?? 0, Data?.Z ?? 0);
        set
        {
            if (Data == null)
            {
                return;
            }

            Data.X = value.x;
            Data.Y = value.y;
            Data.Z = value.z;
            _dirtyFields |= GameEntityFields.Position;
        }
    }

    public Vector3 Velocity
    {
        get => _velocity;
        set
        {
            _velocity = value;
            _dirtyFields |= GameEntityFields.Velocity;
        }
    }

    public Vector3 Orientation
    {
        get => new(0, Data?.Rotation ?? 0, 0);
        set
        {
            if (Data != null)
            {
                Data.Rotation = value.y;
                _dirtyFields |= GameEntityFields.Orientation;
            }
        }
    }

    public string Name
    {
        get => Data?.Name ?? string.Empty;
        set
        {
            if (Data != null)
            {
                Data.Name = value;
            }
        }
    }

    public MapId Map
    {
        get => Data?.Map ?? 0;
        set
        {
            if (Data != null)
            {
                Data.Map = value;
            }
        }
    }

    public ulong Experience
    {
        get => Data?.Experience ?? 0;
        set
        {
            if (Data != null)
            {
                Data.Experience = value;
                _dirtyFields |= GameEntityFields.Experience;
            }
        }
    }

    public ulong RequiredExperience
    {
        get => _requiredExperience;
        set
        {
            _requiredExperience = value;
            _dirtyFields |= GameEntityFields.RequiredExperience;
        }
    }

    public ushort Level
    {
        get => Data?.Level ?? 0;
        set
        {
            if (Data != null)
            {
                Data.Level = value;
                _dirtyFields |= GameEntityFields.Level;
            }
        }
    }

    public void OnDisconnected() => CharacterDisconnected?.Invoke(this);

    public float GetMovementSpeed() => MovementSpeed;

    public void Update(TimeSpan deltaTime)
    {
        Spells.Update(deltaTime);

        // Track cast-suppression window: as long as a spell is casting, keep refreshing the timer.
        if (Spells.IsCasting)
        {
            _lastCastTime = DateTime.UtcNow;
        }

        float dt = (float)deltaTime.TotalSeconds;

        // Health regeneration (skipped if dead or in combat)
        if (!IsInCombat && CurrentHealth > 0 && CurrentHealth < Health && Stamina > 0)
        {
            uint regen = (uint)Math.Max(1f, Stamina * _regenConfig.HealthRegenOutOfCombatPerStamina * dt);
            CurrentHealth = Math.Min(Health, CurrentHealth + regen);
        }

        // Power regeneration (Mana / Energy only; Fury is deferred)
        if (CurrentPower.HasValue && Power.HasValue &&
            CurrentPower.Value < Power.Value &&
            RegenStat > 0 &&
            PowerType is PowerType.Mana or PowerType.Energy)
        {
            bool castSuppressed =
                _lastCastTime != DateTime.MinValue &&
                (DateTime.UtcNow - _lastCastTime).TotalSeconds < _regenConfig.PowerRegenCastSuppressSeconds;

            if (!castSuppressed)
            {
                float coeff = IsInCombat
                    ? _regenConfig.PowerRegenInCombatPerStat
                    : _regenConfig.PowerRegenOutOfCombatPerStat;

                uint regen = (uint)Math.Max(1f, RegenStat * coeff * dt);
                CurrentPower = Math.Min(Power.Value, CurrentPower.Value + regen);
            }
        }
    }

    public Guid InstanceId
    {
        get => System.Guid.TryParse(Data?.InstanceId, out Guid g) ? g : System.Guid.Empty;
        set
        {
            if (Data != null)
            {
                Data.InstanceId = value.ToString();
            }
        }
    }

    public static event UnitFinishedCastAnimationDelegate? OnUnitFinishedCastAnimation;
    public static event UnitAttackAnimationDelegate? OnUnitAttackAnimation;
    public static event CharacterDisconnectedDelegate? CharacterDisconnected;
    public static event UnitInterruptedCastAnimationDelegate? OnUnitInterruptedCastAnimation;
    public static event UnitDamagedDelegate? OnUnitDamaged;
    public static event UnitDamagedDelegate? OnSelfDamaged;

    private void CalculateMovementSpeed()
    {
        const float baseSpeed = 1.0f;
        // TODO: apply equipment + buff modifiers here once stat aggregation lands.
        MovementSpeed = baseSpeed;
    }
}
