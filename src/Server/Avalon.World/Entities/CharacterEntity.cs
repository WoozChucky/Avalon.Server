using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.World.Configuration;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;
using Avalon.World.Abilities;
using Avalon.World.Characters;
using Avalon.World.Inventory;
using Avalon.World.Persistence;
using Avalon.World.Vendors;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Entities;

public class CharacterEntity : ICharacter
{
    private readonly CharacterInventoryContainer _bag;
    private readonly CharacterInventoryContainer _bank;
    private readonly CharacterInventoryContainer _equipment;

    private readonly ILogger<CharacterEntity> _logger;
    private readonly RegenConfiguration _regenConfig;

    // Dirty-field tracking (matches Creature pattern; initialize to None so first consume is a no-op)
    private GameEntityFields _dirtyFields = GameEntityFields.None;

    // Power regen cast-suppression (5-second rule)
    private DateTime _lastCastTime = DateTime.MinValue;

    // Combat state
    private DateTime _lastCombatTime = DateTime.MinValue;

    // The container's clock (#614): the combat tag and the cast-regen suppression time by it.
    private readonly TimeProvider _time = TimeProvider.System;

    public CharacterEntity()
    {
        _logger = null!;
        _equipment = null!;
        _bag = null!;
        _bank = null!;
        Spells = null!;
        _regenConfig = new RegenConfiguration();
    }

    /// <param name="time">The container's clock, the one the instance and combat time by (#614).</param>
    public CharacterEntity(ILoggerFactory loggerFactory, Character character,
        RegenConfiguration regenConfig, TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _logger = loggerFactory.CreateLogger<CharacterEntity>();
        Data = character;
        _equipment = new CharacterInventoryContainer(loggerFactory, InventoryType.Equipment);
        _bag = new CharacterInventoryContainer(loggerFactory, InventoryType.Bag);
        _bank = new CharacterInventoryContainer(loggerFactory, InventoryType.Bank);
        Spells = new CharacterAbilityContainer(loggerFactory);
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

    /// <summary>What the stats calculator last derived; null until the first calculation.</summary>
    public DerivedCharacterStats? Stats { get; private set; }

    /// <summary>
    /// Tick thread. Writes derived stats: the maximums (which the row stores and replication sends),
    /// the current pools per <paramref name="current" />, the regen attributes, and a mark so the
    /// next save writes the CharacterStats row. Refill is for select and level-up; KeepShare is for a
    /// gear change, and keeps the same share of each pool.
    /// </summary>
    public void ApplyStats(DerivedCharacterStats stats, CurrentValues current)
    {
        uint oldHealth = Health;
        uint oldPower = Power ?? 0;

        Health = stats.MaxHealth;
        Power = stats.MaxPower;

        if (current == CurrentValues.Refill)
        {
            CurrentHealth = stats.MaxHealth;
            CurrentPower = stats.MaxPower;
        }
        else
        {
            CurrentHealth = CharacterStatsCalculator.KeepShare(CurrentHealth, oldHealth, stats.MaxHealth);
            CurrentPower = CharacterStatsCalculator.KeepShare(CurrentPower ?? 0, oldPower, stats.MaxPower);
        }

        Stamina = stats.Stamina;
        RegenStat = Class switch
        {
            CharacterClass.Wizard or CharacterClass.Healer => stats.Intellect,
            CharacterClass.Hunter => stats.Agility,
            _ => 0,
        };

        Stats = stats;
        SaveState.StatsChanged();
    }

    public bool IsInCombat =>
        _lastCombatTime != DateTime.MinValue &&
        (_time.GetUtcNow().UtcDateTime - _lastCombatTime).TotalSeconds < _regenConfig.CombatLeaveDelaySeconds;

    public ICharacterGameState CharacterGameState { get; }

    public ICharacterInventory this[InventoryType type] => Container(type);

    /// <summary>
    /// The mutable container behind the read-only indexer. Only the inventory service writes through
    /// it, because it also marks <see cref="SaveState" /> and <see cref="ClientChanges" />.
    /// </summary>
    public CharacterInventoryContainer Container(InventoryType type) => type switch
    {
        InventoryType.Equipment => _equipment,
        InventoryType.Bag => _bag,
        InventoryType.Bank => _bank,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    /// <summary>Slots and money the client must be told about at the end of this tick.</summary>
    public InventoryClientChanges ClientChanges { get; } = new();

    public ICharacterAbilities Spells { get; }

    /// <summary>What the next save must write. Marked by the inventory service and the wallet.</summary>
    public SaveStateTracker SaveState { get; } = new();

    /// <summary>World-side only, not on ICharacter: the modding API cannot read or set PvP (#164).</summary>
    public bool PvpEnabled => Data?.PvpEnabled ?? false;

    public DateTime? PvpOffAt => Data?.PvpOffAt;

    /// <summary>
    /// The off time this character's client was last told in SMSG_PVP_STATE, null for none (#164). Set
    /// only by PvpToggle.Send; never saved, so every session starts untold. World-side only.
    /// </summary>
    public DateTime? PvpOffAtTold { get; internal set; }

    /// <summary>Called by PvpToggle after it writes the row: replicates the flag and marks the save.</summary>
    public void MarkPvpChanged()
    {
        _dirtyFields |= GameEntityFields.PvpEnabled;
        SaveState.PvpChanged();
    }

    /// <summary>Time left until the next periodic save; null until the character first ticks in a map.</summary>
    public TimeSpan? NextPeriodicSaveIn { get; set; }

    /// <summary>
    /// The NPC the bank was opened with (spec #463), or null. The bank is open only while the
    /// connection's current conversation is with this NPC (BankAccess.IsOpen), so a conversation
    /// that ends or changes closes the bank whatever this still says.
    /// </summary>
    public ObjectGuid? OpenBankNpc { get; set; }

    /// <summary>
    /// The NPC whose shop is open (spec #432), or null. Exactly like <see cref="OpenBankNpc" />:
    /// the shop is open only while the connection's current conversation is with this NPC
    /// (ShopAccess.IsOpen), so a conversation that ends or changes closes the shop, whatever this
    /// still says.
    /// </summary>
    public ObjectGuid? OpenShopNpc { get; set; }

    /// <summary>
    /// Closes the bank and the shop together, and forgets any list the shop was owed. Every way a
    /// conversation ends calls this, so neither window can outlive the conversation it opened in.
    /// </summary>
    public void CloseNpcWindows()
    {
        OpenBankNpc = null;
        OpenShopNpc = null;
        VendorListOwed = false;
    }

    /// <summary>
    /// This session's last ten sales to a vendor, newest first (spec #432). In memory only. Cleared
    /// when the character leaves the world, so a buyback lost on logout is simply a completed sale.
    /// </summary>
    public VendorBuyback Buyback { get; } = new();

    /// <summary>
    /// A sale or a buyback changed this player's buyback list, so the instance's vendor pass owes
    /// this connection a new SMSG_VENDOR_LIST if its shop is still open (spec #432).
    /// </summary>
    public bool VendorListOwed { get; set; }

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

    public DateTime LastCastStartTime { get; set; } = DateTime.MinValue;

    public float BodyRadius => UnitBody.CharacterRadius;

    public GameEntityFields ConsumeDirtyFields()
    {
        var dirty = _dirtyFields;
        _dirtyFields = GameEntityFields.None;
        return dirty;
    }

    public void MarkCombat() => _lastCombatTime = _time.GetUtcNow().UtcDateTime;

    public void OnHit(IUnit attacker, uint damage) => OnHit(attacker, damage, abilityId: null);

    /// <summary>
    /// Takes <paramref name="damage" /> from <paramref name="attacker" />; <paramref name="abilityId" /> names
    /// the ability that dealt it, or null for a swing. It sends nothing: the combat service tells its own
    /// instance, which sends the hit and the character's own damage packet (#546).
    /// </summary>
    public void OnHit(IUnit attacker, uint damage, AbilityId? abilityId)
    {
        if (IsDead) return; // corpse — no further state changes or broadcast

        _logger.LogInformation("{Name} has been hit by unit {Attacker} for {Damage} damage", Name, attacker.Guid,
            damage);
        MarkCombat();

        if (damage >= CurrentHealth)
        {
            _logger.LogInformation("{Name} has died", Name);
            CurrentHealth = 0;
            IsDead = true;
        }
        else
        {
            CurrentHealth -= damage;
        }
    }

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

    public CharacterClass Class => Data?.Class ?? default;

    public CharacterGender Gender => Data?.Gender ?? default;

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

    private bool _isDead;
    public bool IsDead
    {
        get => _isDead;
        set
        {
            if (_isDead == value) return;
            _isDead = value;
            _dirtyFields |= GameEntityFields.IsDead;

            // A corpse is at rest (#424). Input from a dead character is dropped before it can write
            // Velocity, so without this the velocity it died with would be broadcast until respawn
            // and other clients would extrapolate the corpse onwards.
            if (value)
                Velocity = Vector3.zero;
        }
    }

    public void Revive()
    {
        // Clear flag first so no invariant observes "alive but at 0 HP" or "dead but at full HP".
        IsDead = false;
        CurrentHealth = Health;
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

    public float GetMovementSpeed() => MovementSpeed;

    public void Update(TimeSpan deltaTime)
    {
        Spells.Update(deltaTime);

        // Track cast-suppression window: as long as a spell is casting, keep refreshing the timer.
        if (Spells.IsCasting)
        {
            _lastCastTime = _time.GetUtcNow().UtcDateTime;
        }

        float dt = (float)deltaTime.TotalSeconds;

        // Health regeneration (skipped if dead or in combat)
        if (!IsInCombat && !IsDead && CurrentHealth > 0 && CurrentHealth < Health && Stamina > 0)
        {
            uint regen = (uint)Math.Max(1f, Stamina * _regenConfig.HealthRegenOutOfCombatPerStamina * dt);
            CurrentHealth = Math.Min(Health, CurrentHealth + regen);
        }

        // Power regeneration (Mana / Energy only; Fury is deferred)
        if (!IsDead && CurrentPower.HasValue && Power.HasValue &&
            CurrentPower.Value < Power.Value &&
            RegenStat > 0 &&
            PowerType is PowerType.Mana or PowerType.Energy)
        {
            bool castSuppressed =
                _lastCastTime != DateTime.MinValue &&
                (_time.GetUtcNow().UtcDateTime - _lastCastTime).TotalSeconds < _regenConfig.PowerRegenCastSuppressSeconds;

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

    private void CalculateMovementSpeed()
    {
        const float baseSpeed = 4.0f;
        // TODO: apply equipment + buff modifiers here once stat aggregation lands.
        MovementSpeed = baseSpeed;
    }
}
