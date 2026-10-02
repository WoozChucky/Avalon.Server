using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.World.Abilities;
using Avalon.World.Characters;
using Avalon.World.Configuration;
using Avalon.World.Inventory;
using Avalon.World.Items;
using Avalon.World.Parties;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;
using Avalon.World.Quests;
using Avalon.World.Social;
using Avalon.World.Threading;
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

    // Fury decay (#526): points lost per second out of combat, and the fraction of a point owed but
    // not yet taken, carried between ticks. World-side and never saved.
    private readonly float _furyDecayPerSecond = GameConfiguration.DefaultFuryDecayPerSecond;
    private double _furyDecayRemainder;
    private const double FuryDecayTolerance = 1e-3;

    // Mana and Energy regeneration (PowerRegen): the fraction of a point earned but not yet given, carried between
    // ticks so the per-second rate holds at any tick rate; dropped whenever the pool cannot regenerate, full included.
    private double _powerRegenCarry;

    // Out-of-combat health regeneration: the same carry, dropped whenever health cannot regenerate (in combat,
    // dead, full, or no Stamina), so a full pool banks nothing and a fight starts the fraction over.
    private double _healthRegenCarry;

    public CharacterEntity()
    {
        Quests = new QuestLog(SaveState);
        Ignores = new IgnoreList(SaveState);
        _logger = null!;
        _equipment = null!;
        _bag = null!;
        _bank = null!;
        Spells = null!;
        _regenConfig = new RegenConfiguration();
    }

    /// <param name="time">The container's clock, the one the instance and combat time by (#614).</param>
    /// <param name="furyDecayPerSecond">Game:FuryDecayPerSecond (#526); the setting's default when omitted.</param>
    /// <param name="tickThread">The tick-thread assertion its ignore list makes (#639); none when omitted.</param>
    public CharacterEntity(ILoggerFactory loggerFactory, Character character,
        RegenConfiguration regenConfig, TimeProvider? time = null,
        float furyDecayPerSecond = GameConfiguration.DefaultFuryDecayPerSecond, TickThreadGuard? tickThread = null)
    {
        Quests = new QuestLog(SaveState);
        Ignores = new IgnoreList(SaveState, tickThread);
        _time = time ?? TimeProvider.System;
        _furyDecayPerSecond = furyDecayPerSecond;
        _logger = loggerFactory.CreateLogger<CharacterEntity>();
        Data = character;
        _equipment = new CharacterInventoryContainer(loggerFactory, InventoryType.Equipment);
        _bag = new CharacterInventoryContainer(loggerFactory, InventoryType.Bag);
        _bank = new CharacterInventoryContainer(loggerFactory, InventoryType.Bank);
        Spells = new CharacterAbilityContainer(loggerFactory);
        Guid = new ObjectGuid(ObjectType.Character, character.Id);
        _regenConfig = regenConfig;
    }

    public Character? Data { get; init; }

    /// <summary>
    /// Metres per second (#627): the base until the first stats refresh, then what that refresh derived from
    /// the gear and the combat formula's bounds. Never 0 at the default, which would pin the player.
    /// </summary>
    private float MovementSpeed { get; set; } = CharacterMovement.BaseSpeed;

    /// <summary>
    /// The haste this character casts with (#627), in percentage points: its gear's AttackSpeed total, from 0
    /// up to the combat formula's HasteCap, fixed at the last stats refresh. The cast system reads it once
    /// per cast. World-side and read-only, not on ICharacter: the modding API cannot change it (#622).
    /// </summary>
    public float EffectiveHastePct { get; private set; }

    public DateTime EnteredWorld { get; set; }

    public uint Stamina { get; set; }
    public uint RegenStat { get; set; }

    /// <summary>What the stats calculator last derived; null until the first calculation.</summary>
    public DerivedCharacterStats? Stats { get; private set; }

    /// <summary>
    /// Tick thread. Writes derived stats: the maximums (which the row stores and replication sends),
    /// the current pools per <paramref name="current" />, the regen attributes, and a mark so the
    /// next save writes the CharacterStats row. EnterWorld is for select, Refill for a level-up, and
    /// KeepShare for a gear change, which keeps the same share of each pool. Fury is not a fill-up pool
    /// (#526): EnterWorld empties it, and Refill and KeepShare keep its value, capped at the new maximum.
    /// </summary>
    /// <param name="formula">
    /// Bounds haste and movement speed (#627): the current generation's, so a /reload combat reaches the
    /// character at this refresh. Required, so no caller can fall back to the seeded values unnoticed.
    /// </param>
    public void ApplyStats(DerivedCharacterStats stats, CurrentValues current, Avalon.Domain.World.CombatFormula formula)
    {
        uint oldHealth = Health;
        uint oldPower = Power ?? 0;

        Health = stats.MaxHealth;
        Power = stats.MaxPower;

        // The maximums as stored, clamped to the row's int (#506): the pools fill to these, never past them.
        uint maxHealth = Health;
        uint maxPower = Power ?? 0;

        if (current == CurrentValues.KeepShare)
        {
            CurrentHealth = CharacterStatsCalculator.KeepShare(CurrentHealth, oldHealth, maxHealth);
        }
        else
        {
            CurrentHealth = maxHealth;
        }

        if (PowerType == PowerType.Fury)
        {
            // The fraction of decay owed is forgotten only when Fury is set: entering the world, or a cap
            // that lowers it. A gear change that leaves it alone must not, or swapping gear quickly
            // enough would stop the decay.
            uint fury = CurrentPower ?? 0;
            uint kept = current == CurrentValues.EnterWorld ? 0u : Math.Min(fury, maxPower);
            if (current == CurrentValues.EnterWorld || kept != fury)
                _furyDecayRemainder = 0d;
            CurrentPower = kept;
        }
        else if (current == CurrentValues.KeepShare)
        {
            CurrentPower = CharacterStatsCalculator.KeepShare(CurrentPower ?? 0, oldPower, maxPower);
        }
        else
        {
            CurrentPower = maxPower;
        }

        Stamina = stats.Stamina;
        RegenStat = PowerRegen.StatOf(Class, stats);

        // #627: both bounded by the formula this refresh read, so a combat reload reaches them at the next refresh.
        EffectiveHastePct = stats.EffectiveHastePct(formula);
        MovementSpeed = CharacterMovement.SpeedFor(stats.MovementSpeedPct, formula);

        Stats = stats;
        SaveState.StatsChanged();
    }

    /// <summary>
    /// What this character attacks with (#506): its level, and the damage stats, crit and main-hand weapon
    /// range the last stats refresh derived (select, gear change, level-up). World-side and read-only, not
    /// on ICharacter: the modding API cannot change what a hit deals (#622).
    /// </summary>
    internal AttackerCombat Combat => Stats is { } s
        ? s.AttackerAt(Level)
        : new AttackerCombat(Level, 0, 0, 0f, 0, 0);

    /// <summary>
    /// The character sheet this character's client was last sent (#506), null before the first. World-side
    /// and never saved: a new session starts with none, so it is sent the whole sheet again.
    /// </summary>
    public CharacterSheet? SheetSent { get; internal set; }

    /// <summary>
    /// The per-hit ability amounts this character's client was last told (#669), in ability load order, and
    /// the combat stats they were worked out from. World-side and never saved. Set at select, where the
    /// abilities packet carries them, and by <c>AbilityAmountsFlusher</c> whenever they change.
    /// </summary>
    internal AbilityAmount[]? AbilityAmountsSent { get; set; }

    internal AttackerCombat? AbilityAmountsSentFor { get; set; }

    /// <summary>What this character defends with (#506): armour, dodge and block, from the last stats refresh.</summary>
    internal DefenderCombat Defence => Stats is { } s ? s.Defence : default;

    public bool IsInCombat =>
        _lastCombatTime != DateTime.MinValue &&
        (_time.GetUtcNow().UtcDateTime - _lastCombatTime).TotalSeconds < _regenConfig.CombatLeaveDelaySeconds;

    /// <summary>
    /// What this character's client has been told exists. World-side, not on ICharacter (#612); its
    /// instance diffs it every tick, and resets it when the character enters (#611).
    /// </summary>
    public CharacterCharacterGameState CharacterGameState { get; } = new();

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

    /// <summary>
    /// The character's quests (#433). World-side, never on ICharacter: the modding API reaches quests only through
    /// a QuestScript's IQuestContext. Loaded at select; every change marks <see cref="SaveState" />.
    /// </summary>
    public QuestLog Quests { get; }

    /// <summary>
    /// The characters this one ignores (#723). World-side, never on ICharacter, so no mod can read or change it.
    /// Loaded at select; every change marks <see cref="SaveState" />.
    /// </summary>
    public IgnoreList Ignores { get; }

    /// <summary>World-side only, not on ICharacter: the modding API cannot read or set PvP (#164).</summary>
    public bool PvpEnabled => Data?.PvpEnabled ?? false;

    public DateTime? PvpOffAt => Data?.PvpOffAt;

    /// <summary>
    /// The off time this character's client was last told in SMSG_PVP_STATE, null for none (#164). Set
    /// only by PvpToggle.Send; never saved, so every session starts untold. World-side only.
    /// </summary>
    public DateTime? PvpOffAtTold { get; internal set; }

    /// <summary>
    /// The party this character is in, or null (2026-09-30). Set only by PartyService, on the tick; World-side,
    /// never on ICharacter, so no mod can put two players on the same side. Hostility reads it.
    /// </summary>
    public PartyId? PartyId { get; internal set; }

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
    /// Item use cooldowns (2026-10-02). World-side and never saved: a new session starts with none. Not on ICharacter,
    /// so the modding API cannot clear them.
    /// </summary>
    public ItemCooldowns ItemCooldowns { get; } = new();

    /// <summary>
    /// A sale or a buyback changed this player's buyback list, so the instance's vendor pass owes
    /// this connection a new SMSG_VENDOR_LIST if its shop is still open (spec #432).
    /// </summary>
    public bool VendorListOwed { get; set; }

    /// <summary>
    /// The <see cref="QuestLog.HeldVersion" /> the last SMSG_VENDOR_LIST was built at (#738). While the shop is open,
    /// a different one means an accept, an abandon or a turn-in since, which can meet or unmeet a row's quest gate,
    /// so the vendor pass sends the list again. Never saved.
    /// </summary>
    public int VendorListQuestVersion { get; set; }

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
                // #506: the row stores an int, so a gear total past int.MaxValue is clamped, never wrapped negative.
                Data.Health = (int)Math.Min(value, (uint)int.MaxValue);
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
                // #506: clamped to the int the row stores, as Health is.
                Data.Power1 = (int)Math.Min(value ?? 0u, (uint)int.MaxValue);
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

    /// <summary>Temporary staff combat override; never copied to the persisted character row.</summary>
    public bool GodMode { get; set; }

    public float BodyRadius => UnitBody.CharacterRadius;

    public GameEntityFields ConsumeDirtyFields()
    {
        var dirty = _dirtyFields;
        _dirtyFields = GameEntityFields.None;
        return dirty;
    }

    public void MarkCombat() => _lastCombatTime = _time.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Adds <paramref name="amount" /> to the current power, capped at the maximum (#526). Only a pool a
    /// cast spends (Mana, Energy or Fury) gains, and a dead character gains nothing. World-side only, not
    /// on ICharacter or IUnit: the modding API cannot grant power.
    /// </summary>
    internal void GainPower(uint amount)
    {
        uint current = CurrentPower ?? 0;
        uint next = PowerPool.Gain(PowerType, IsDead, current, Power ?? 0, amount);
        if (next != current) CurrentPower = next;
    }

    /// <summary>
    /// Empties a Fury pool and forgets any fraction of decay owed (#526): the one place death and every
    /// instance transfer set Fury. A Mana or Energy pool is left alone. World-side only.
    /// </summary>
    internal void ResetFury()
    {
        _furyDecayRemainder = 0d;
        if (PowerPool.EmptiesOnReset(PowerType) && CurrentPower != 0)
            CurrentPower = 0;
    }

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

    /// <summary>
    /// The row's name. Settable here, World-side, for tests and tooling only (#757): <see cref="ICharacter.Name" /> on
    /// the modding API is get-only, and a changed name is never saved (the column is insert-only to the change
    /// tracker; a rename is <c>ICharacterRepository.TryRenameAsync</c>).
    /// </summary>
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
            {
                Velocity = Vector3.zero;

                // Death empties Fury (#526), whatever set the flag.
                ResetFury();
            }
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

        // Health regeneration (skipped if dead or in combat). The fraction of a point is carried between ticks,
        // as power's is, and dropped whenever health cannot regenerate.
        if (!IsInCombat && !IsDead && CurrentHealth > 0 && CurrentHealth < Health && Stamina > 0)
        {
            uint regen = PowerRegen.TakeWholePoints(
                Stamina * (double)_regenConfig.HealthRegenOutOfCombatPerStamina * deltaTime.TotalSeconds,
                ref _healthRegenCarry);
            if (regen > 0)
            {
                CurrentHealth = Math.Min(Health, CurrentHealth + regen);
            }
        }
        else
        {
            _healthRegenCarry = 0d;
        }

        // Fury (#526) never regenerates. Out of combat it drains, carrying the fraction of a point owed
        // between ticks so that a per-second rate is lost exactly at any tick rate; in combat it holds,
        // and the fraction starts over when the fight ends.
        if (PowerType == PowerType.Fury)
        {
            if (IsInCombat || IsDead || _furyDecayPerSecond <= 0f || (CurrentPower ?? 0) == 0)
            {
                _furyDecayRemainder = 0d;
            }
            else
            {
                _furyDecayRemainder += _furyDecayPerSecond * deltaTime.TotalSeconds;

                // A 1/60 s TimeSpan is truncated to whole ticks, so sixty of them fall a few millionths of
                // a second short of one; the tolerance keeps that from costing a whole point. What is
                // left may dip that far below 0 and is repaid by the next tick.
                double owed = Math.Floor(_furyDecayRemainder + FuryDecayTolerance);
                uint whole = owed >= uint.MaxValue ? uint.MaxValue : (uint)owed;
                if (whole > 0)
                {
                    _furyDecayRemainder -= whole;
                    uint current = CurrentPower ?? 0;
                    CurrentPower = whole >= current ? 0u : current - whole;
                }
            }
        }

        // Power regeneration (Mana / Energy only). The fraction of a point is carried between ticks
        // (PowerRegen.Amount), and dropped whenever the pool cannot regenerate, so a full pool banks nothing.
        if (!IsDead && CurrentPower.HasValue && Power.HasValue &&
            CurrentPower.Value < Power.Value &&
            RegenStat > 0 &&
            PowerType is PowerType.Mana or PowerType.Energy)
        {
            bool castSuppressed =
                _lastCastTime != DateTime.MinValue &&
                (_time.GetUtcNow().UtcDateTime - _lastCastTime).TotalSeconds < _regenConfig.PowerRegenCastSuppressSeconds;

            uint regen = PowerRegen.Amount(_regenConfig, RegenStat, IsInCombat, castSuppressed,
                deltaTime.TotalSeconds, ref _powerRegenCarry);
            if (regen > 0)
            {
                CurrentPower = Math.Min(Power.Value, CurrentPower.Value + regen);
            }
        }
        else
        {
            _powerRegenCarry = 0d;
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

}
