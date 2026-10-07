using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.State;
using Avalon.World.Auras;
using Avalon.World.Creatures;
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

    /// <summary>Percentage points.</summary>
    public float DodgePct { get; init; }

    /// <summary>Percentage points.</summary>
    public float BlockPct { get; init; }

    /// <summary>
    /// Seconds between swings with no haste (#627): the template's BaseAttackTime, fixed at spawn by
    /// <see cref="CreatureSpawner" />. The seeded 2.25 for a creature built without one.
    /// </summary>
    public float BaseAttackTime { get; init; } = DefaultBaseAttackTime;

    /// <summary>The seeded swing interval, and what a creature that is not this World-side type swings at.</summary>
    public const float DefaultBaseAttackTime = 2.25f;

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
    /// Takes the auras' totals: its haste (from none, since nothing else sets one) and its maximum health; its attack,
    /// defence and speed read them as they go.
    /// </summary>
    public void ApplyAuraStats(AuraStatTotals totals)
    {
        AuraTotals = totals;
        HastePct = AuraStats.Apply(0f, totals, AuraStat.HastePct);
        RecomputeMaxHealth();
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
    /// What this creature attacks with (#506): its level, its crit, and its natural damage range in place of a weapon
    /// (#163), with its auras folded in.
    /// </summary>
    internal AttackerCombat Combat => AuraStats.Fold(new AttackerCombat(Level, 0, 0, CritPct, DamageMin, DamageMax), AuraTotals);

    /// <summary>What this creature defends with (#506), its auras folded in.</summary>
    internal DefenderCombat Defence => AuraStats.Fold(new DefenderCombat(Armor, DodgePct, BlockPct), AuraTotals);

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

    public AiScript? Script { get; set; }

    public IReadOnlyList<PatrolPoint> PatrolPath { get; set; } = [];

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
        set
        {
            // A creature is dead at 0 health (#672), so reaching 0, or leaving it, changes its death
            // state too: marked, so the broadcast that carries the 0 carries IsDead with it.
            if ((_currentHealth == 0) != (value == 0)) _dirtyFields |= GameEntityFields.IsDead;
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

    public void OnHit(IUnit attacker, uint damage) => Script?.OnHit(attacker, damage);
}
