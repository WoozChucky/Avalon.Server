using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.World.Abilities.Targeting;
using Avalon.World.Auras;
using Avalon.World.Creatures;
using Avalon.World.Instances;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Scripts;
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
    private AiScript? _script;
    private IReadOnlyList<PatrolPoint> _patrolPath = [];

    public CreatureTemplateId TemplateId { get; set; } = null!;
    public ObjectGuid Guid { get; set; }
    public ICreatureMetadata Metadata { get; set; }
    public string Name { get; set; } = string.Empty;
    public float Speed { get; set; }
    public string ScriptName { get; set; } = string.Empty;
    public bool Invulnerable { get; set; }

    /// <summary>
    /// Whether interacting with this creature will do something, fixed by
    /// <see cref="CreatureSpawner"/> from <see cref="Dialogue.NpcInteraction.CanInteract"/> and
    /// replicated as <c>ObjectState.CanInteract</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately not on <see cref="ICreature"/>: <c>Avalon.World.Public</c> is the modding API,
    /// and a mod must not be able to make a creature advertise an interaction the server would
    /// refuse. Init-only, because it is decided once at spawn: a <c>/reload dialogue</c> reaches
    /// only creatures spawned after it. A town NPC, in a persistent instance and never respawned,
    /// keeps its flag until the server restarts.
    /// </remarks>
    public bool CanInteract { get; init; }

    /// <summary>
    /// How dangerous this creature is, fixed by <see cref="CreatureSpawner"/> from its template's
    /// <c>Rarity</c> and replicated as <c>ObjectState.Rarity</c> (#709) for a rarity-coloured nameplate.
    /// </summary>
    /// <remarks>
    /// Deliberately not on <see cref="ICreature"/>, for the reason <see cref="CanInteract"/> is not: a mod
    /// must not be able to change the rarity a creature advertises. Init-only, because it is decided once at
    /// spawn: a <c>/reload creatures</c> reaches only creatures spawned after it. It says what the client is
    /// told and nothing else; the stats the rarity scaled were fixed at spawn by <c>CreatureStatDeriver</c>.
    /// </remarks>
    public Avalon.World.Public.Enums.CreatureRarity Rarity { get; init; }

    /// <summary>
    /// Which model a client draws this creature with, fixed by <see cref="CreatureSpawner"/> from its template's
    /// <c>DisplayId</c> and replicated as <c>ObjectState.DisplayId</c> (#1022). 0 is none: the client keeps its
    /// default.
    /// </summary>
    /// <remarks>
    /// Deliberately not on <see cref="ICreature"/>, for the reason <see cref="Rarity"/> is not: a mod must not be
    /// able to change what a client sees. Init-only, because it is decided once at spawn: a
    /// <c>/reload creatures</c> reaches only creatures spawned after it.
    /// </remarks>
    public uint DisplayId { get; init; }

    /// <summary>Metres. Copied from <c>CreatureTemplate.BodyRadius</c> by <see cref="CreatureSpawner"/>.</summary>
    public float BodyRadius { get; init; } = UnitBody.DefaultCreatureRadius;

    // No dirty-field tracking on these three, unlike Health and Level: none is broadcast in entity
    // state. The client learns damage from combat packets and never sees a creature's experience value.
    public uint DamageMin { get; set; }
    public uint DamageMax { get; set; }
    public uint Experience { get; set; }

    /// <summary>
    /// The maximum health <see cref="CreatureStatDeriver" /> gave it at spawn, never changed (2026-09-30). 0 means
    /// unknown (a creature built without the spawner), and such a creature is never rescaled.
    /// </summary>
    public uint BaseMaxHealth { get; init; }

    // What combat resolves this creature's hits with (#506), fixed at spawn by CreatureSpawner from
    // CreatureStatDeriver. World-side and init-only, deliberately not on ICreature: the modding API
    // cannot change a creature's defences. A /reload creatures reaches only creatures spawned after it.

    /// <summary>Reduces the hits it takes.</summary>
    public uint Armor { get; init; }

    /// <summary>Percentage points.</summary>
    public float CritPct { get; init; }

    /// <summary>The evasion rating (#1178), weighed against the attacker's level.</summary>
    public uint Evasion { get; init; }

    /// <summary>Percentage points.</summary>
    public float BlockPct { get; init; }

    /// <summary>The type of a hit whose ability names none (#1178): its template's.</summary>
    public DamageType DamageType { get; init; }

    /// <summary>Percent per element (#1178), as its template authored them; its auras fold in on top.</summary>
    public Resistances Resistances { get; init; }

    /// <summary>
    /// The most its ward holds before its auras (#1178), from <see cref="CreatureStatDeriver" />. Setting it at spawn fills
    /// the ward.
    /// </summary>
    public uint BaseWard
    {
        get => _baseWard;
        init
        {
            _baseWard = value;
            _ward.Reset(value);
        }
    }

    private readonly uint _baseWard;

    /// <summary>When its ward recharges (#1178): the world's Regen settings, fixed at spawn; the defaults without the spawner.</summary>
    internal RegenConfiguration WardRegen { get; init; } = s_defaultRegen;

    private static readonly RegenConfiguration s_defaultRegen = new();

    // Its ward (#1178), in front of its health: filled at spawn and at its reset at home, its share kept when an aura changes
    // its maximum. Party scaling never touches it. World-side and never on ICreature.
    private WardPool _ward;

    /// <summary>What its ward holds now (#1178). Read-only and World-side, not on ICreature.</summary>
    public uint Ward => _ward.Current;

    /// <summary>What a full ward holds (#1178): its base with its auras folded in.</summary>
    public uint MaxWard => _ward.Max;

    /// <summary>
    /// The ward takes what it can of <paramref name="absorbable" /> (#1178), after recharging to <paramref name="now" />,
    /// and pauses its recharge; answers what it took.
    /// </summary>
    internal uint AbsorbWithWard(uint absorbable, DateTime now)
    {
        if (_ward.Max == 0 || absorbable == 0)
            return 0;

        AdvanceWard(now);
        uint taken = _ward.Absorb(absorbable, now);
        if (taken > 0)
            _dirtyFields |= GameEntityFields.Ward;
        return taken;
    }

    /// <summary>Damage taken, whether the ward met it or not (#1178): the recharge pauses from <paramref name="now" />.</summary>
    internal void WardDamaged(DateTime now)
    {
        if (_ward.Max == 0)
            return;

        AdvanceWard(now);
        _ward.Damaged(now);
    }

    /// <summary>
    /// Recharges the ward to what <paramref name="now" /> allows (#1178): the instance calls it before the creature's script
    /// runs, for a creature with a ward, and a hit before it meets the ward. A corpse's ward does not recharge.
    /// </summary>
    internal void AdvanceWard(DateTime now)
    {
        if (CurrentHealth > 0 && _ward.Advance(now, WardRegen))
            _dirtyFields |= GameEntityFields.Ward;
    }

    /// <summary>Fills the ward (#1178): its reset at home.</summary>
    internal void ResetWard()
    {
        if (_ward.Current == _ward.Max)
            return;

        _ward.Reset(_ward.Max);
        _dirtyFields |= GameEntityFields.Ward;
    }

    /// <summary>
    /// Seconds between swings with no haste (#627): the template's BaseAttackTime, fixed at spawn by
    /// <see cref="CreatureSpawner" />. The seeded 2.25 for a creature built without one.
    /// </summary>
    public float BaseAttackTime { get; init; } = DefaultBaseAttackTime;

    /// <summary>The seeded swing interval, and what a creature that is not this World-side type swings at.</summary>
    public const float DefaultBaseAttackTime = 2.25f;

    /// <summary>
    /// How much of a stun, root, silence or slow lands on it (#770), fixed at spawn by <see cref="CreatureSpawner" />
    /// from its template's overrides, else its rarity's row; the full duration for a creature built without the spawner.
    /// World-side and init-only, for the reason its defences are: a /reload creatures reaches only creatures spawned
    /// after it.
    /// </summary>
    public ControlResistance ControlResistance { get; init; } = ControlResistance.Full;

    /// <summary>The combat formula's HasteCap when this creature spawned (#627): the most of <see cref="HastePct" /> that counts.</summary>
    public float HasteCap { get; init; } = 50f;

    /// <summary>
    /// Haste in percentage points (#627), 0 until an effect sets it; set by its auras. World-side, deliberately
    /// not on ICreature: the modding API cannot change how fast a creature swings (#622). A change applies from
    /// the next swing; the countdown in progress keeps running.
    /// </summary>
    public float HastePct { get; set; }

    /// <summary>Seconds between swings: BaseAttackTime divided by 1 + haste / 100, haste at most the cap.</summary>
    public float SwingInterval => Haste.Scale(BaseAttackTime, MathF.Min(HastePct, HasteCap));

    /// <summary>
    /// What this creature's auras add to its stats (auras), set by the aura system whenever they change. World-side,
    /// deliberately not on ICreature: no mod can change a creature's stats.
    /// </summary>
    public AuraStatTotals AuraTotals { get; private set; } = AuraStatTotals.Empty;

    /// <summary>The combat formula's movement floor and cap when this creature spawned, in percentage points.</summary>
    public float MoveSpeedFloor { get; init; } = -50f;

    public float MoveSpeedCap { get; init; } = 35f;

    // The party scaling its maximum health was last given, which the health aura composes with.
    private double _healthFactor = 1d;

    /// <summary>
    /// Takes the auras' totals: its haste (from none, since nothing else sets one), its maximum health and its maximum ward
    /// (#1178, keeping its share from <paramref name="now" />); its attack, defence and speed read them as they go.
    /// </summary>
    public void ApplyAuraStats(AuraStatTotals totals, DateTime now)
    {
        AuraTotals = totals;
        HastePct = AuraStats.Apply(0f, totals, AuraStat.HastePct);
        RecomputeMaxHealth();

        uint oldWard = _ward.Current;
        uint oldMax = _ward.Max;
        _ward.SetMax(AuraStats.Apply(BaseWard, totals, AuraStat.MaxWard), now);
        if (_ward.Current != oldWard)
            _dirtyFields |= GameEntityFields.Ward;
        if (_ward.Max != oldMax)
            _dirtyFields |= GameEntityFields.MaxWard;
    }

    /// <summary>
    /// What its walking speed is multiplied by: 1 + its aura movement points, bounded like a character's by the floor and
    /// cap it spawned with. The script keeps setting Speed from its template; both locomotions read this on top.
    /// </summary>
    public float SpeedFactor
    {
        get
        {
            float points = AuraStats.Apply(0f, AuraTotals, AuraStat.MovementSpeed);
            return 1f + MathF.Min(MathF.Max(points, MoveSpeedFloor), MoveSpeedCap) / 100f;
        }
    }

    /// <summary>
    /// Sets the maximum to the base times <paramref name="factor" />, rounded, at least 1, with its health aura on top,
    /// and keeps the share of the pool it had (<see cref="CharacterStatsCalculator.KeepShare" />), so a living creature
    /// stays alive. A creature at 0 health is never rescaled (#672: a corpse stays dead). World-side, not on ICreature:
    /// no mod scales health.
    /// </summary>
    public void Rescale(double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0)
            return;

        _healthFactor = factor;
        RecomputeMaxHealth();
    }

    private void RecomputeMaxHealth()
    {
        if (BaseMaxHealth == 0 || CurrentHealth == 0)
            return;

        double scaled = Math.Round(BaseMaxHealth * _healthFactor, MidpointRounding.AwayFromZero);
        uint factored = scaled >= uint.MaxValue ? uint.MaxValue : Math.Max(1u, (uint)scaled);
        uint max = Math.Max(1u, AuraStats.Apply(factored, AuraTotals, AuraStat.MaxHealth));
        if (max == Health)
            return;

        CurrentHealth = CharacterStatsCalculator.KeepShare(CurrentHealth, Health, max);
        Health = max;
    }

    /// <summary>
    /// What this creature attacks with (#506): its level, its crit, its natural damage range in place of a weapon
    /// (#163) and its base damage type (#1178), with its auras folded in.
    /// </summary>
    internal AttackerCombat Combat =>
        AuraStats.Fold(new AttackerCombat(Level, 0, 0, CritPct, DamageMin, DamageMax, DamageType), AuraTotals);

    /// <summary>What this creature defends with (#506), its resistances (#1178) and its auras folded in.</summary>
    internal DefenderCombat Defence =>
        AuraStats.Fold(new DefenderCombat(Armor, Evasion, BlockPct, Resistances), AuraTotals);

    /// <summary>
    /// The abilities this creature fights with (#163), loaded by its script when it attaches. World-side,
    /// deliberately not on ICreature: the modding API cannot give a creature abilities or read its cast state (#622).
    /// </summary>
    public CreatureAbilities Abilities { get; } = new();

    /// <summary>
    /// The auras on this creature (auras). World-side, deliberately not on ICreature: no mod can add or end them, and
    /// they never save.
    /// </summary>
    public UnitAuras Auras { get; } = new();

    public AiScript? Script
    {
        get => _script;
        set
        {
            _script = value;
            Scheduler?.ScriptChanged(this);
        }
    }

    /// <summary>A new path wakes the creature (#979): a patrol that stood for want of one walks it.</summary>
    public IReadOnlyList<PatrolPoint> PatrolPath
    {
        get => _patrolPath;
        set
        {
            _patrolPath = value;
            Scheduler?.Wake(this);
        }
    }

    /// <summary>
    /// The creature scheduler of the instance this creature is in (#979), set while it is a member; null outside one, or
    /// in an instance that updates every creature every tick. World-side, never on ICreature.
    /// </summary>
    internal CreatureScheduler? Scheduler { get; set; }

    /// <summary>Its place in <see cref="Scheduler" />: what it waits for, and where. The scheduler's alone.</summary>
    internal ref CreatureSchedule Schedule => ref _schedule;

    private CreatureSchedule _schedule;

    /// <summary>
    /// The creature's recent positions by world tick, for lag compensation (#1082): rented from the instance's pool as the
    /// creature joins it and returned as it leaves; null outside an instance, and in one with no world tick count (tests).
    /// World-side, never on ICreature. Public to read for the test assembly (see <see cref="PositionHistory" />).
    /// </summary>
    public PositionHistory? History { get; internal set; }

    /// <summary>
    /// A jump, not a walk (#1082): a teleport (a leash return). Called before the new position is written, it empties
    /// the history, so a query rewound to before the jump never finds the creature anywhere it stood, and has its
    /// instance's grid list it for the next Step 4b, which seeds the history where it lands (even where it already stood)
    /// without counting the jump as a step any unit may have moved.
    /// </summary>
    internal void Jump()
    {
        History?.Invalidate();
        if (_spatial.Grid is { } grid)
            grid.Jumped(_spatial.Slot);
    }

    /// <summary>
    /// Where the instance this creature is in indexes it (#982): set while it is a member and cleared when it leaves, so
    /// <see cref="Position" /> keeps that index current. The grid's alone; World-side, never on ICreature.
    /// </summary>
    internal ref SpatialHandle Spatial => ref _spatial;

    private SpatialHandle _spatial;

    public IUnit? TauntedBy { get; set; }
    public DateTime TauntExpiresAt { get; set; } = DateTime.MinValue;

    public ushort Level
    {
        get => _level;
        set { _level = value; _dirtyFields |= GameEntityFields.Level; }
    }

    public Vector3 Position
    {
        get => _position;
        set
        {
            _position = value;
            _dirtyFields |= GameEntityFields.Position;
            if (_spatial.Grid is { } grid)
                grid.Moved(_spatial.Slot, value);
        }
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
        set
        {
            // A creature is dead at 0 health (#672), so reaching 0, or leaving it, changes its death
            // state too: marked, so the broadcast that carries the 0 carries IsDead with it.
            if ((_currentHealth == 0) != (value == 0)) _dirtyFields |= GameEntityFields.IsDead;

            // #1178: death empties the ward, whatever brought the health to 0 (a hit, a tick that passed the ward, a
            // script). The clock it empties at is moot: a corpse's ward never recharges, and a creature is never revived.
            if (_currentHealth > 0 && value == 0 && _ward.Current > 0)
            {
                _ward.Empty(default);
                _dirtyFields |= GameEntityFields.Ward;
            }

            _currentHealth = value;
            _dirtyFields |= GameEntityFields.CurrentHealth;
        }
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

    public DateTime LastCastStartTime { get; set; } = DateTime.MinValue;

    public GameEntityFields ConsumeDirtyFields()
    {
        GameEntityFields dirty = _dirtyFields;
        _dirtyFields = GameEntityFields.None;
        return dirty;
    }

    public void LookAt(Vector3 target)
    {
        var direction = Vector3.Normalize(target - Position);
        float yawRadians = Mathf.Atan2(direction.x, direction.z);
        float yawDegrees = yawRadians * Mathf.Rad2Deg;
        Orientation = new Vector3(0.0f, yawDegrees, 0.0f);
    }

    public bool IsLookingAt(Vector3 target, float threshold = 0.1f)
    {
        var direction = Vector3.Normalize(target - Position);
        float yawRadians = Mathf.Atan2(direction.x, direction.z);
        float yawDegrees = yawRadians * Mathf.Rad2Deg;
        Vector3 orientation = new(0.0f, yawDegrees, 0.0f);
        return Mathf.Abs(Orientation.y - orientation.y) < threshold;
    }

    /// <summary>Wakes the creature (#979), then tells its script of the hit.</summary>
    public void OnHit(IUnit attacker, uint damage)
    {
        Scheduler?.Wake(this);
        Script?.OnHit(attacker, damage);
    }

    /// <summary>
    /// An attack that dealt nothing (a dodge, a harmful aura's landing or tick, #506): wakes the creature (#979), then
    /// tells its script. World-side; the combat service calls it.
    /// </summary>
    internal void OnAttacked(IUnit attacker)
    {
        Scheduler?.Wake(this);
        Script?.OnAttacked(attacker);
    }

    /// <summary>A character left the instance (#546): wakes the creature (#979), then tells its script.</summary>
    internal void OnCharacterLeft(ICharacter character)
    {
        Scheduler?.Wake(this);
        Script?.OnCharacterLeft(character);
    }
}
