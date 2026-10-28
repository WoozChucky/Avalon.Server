using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Combat;
using Avalon.World.Abilities;
using Avalon.World.Abilities.Targeting;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;

namespace Avalon.World.Scripts.Abilities;

/// <summary>
/// A projectile (#164): a world object spawned at the caster, flying flat toward the aim point at ProjectileSpeed
/// (metres per second, the #424 velocity rule). Each tick it sweeps its step, capped at the Reach left and cut by the
/// navmesh ray, against unit bodies, nearest first, flying through every unit it may not affect
/// (<c>AbilityEffect.Qualifies</c>, #1181): without Pierce it ends on the first unit it may, stopping at the point of
/// its step nearest that unit's centre; with Pierce it hits each unit once and flies on, ending on its MaxTargets-th
/// hit when that is above 0 (#1181). It ends at Reach, at a wall, or on its last hit, and the instance despawns it once
/// its final state has gone out. No target, no homing. It starts where the caster stood when the cast started (#648),
/// the origin of the lane the start broadcast carried. It is fired as circles and cones are, with its lane, and tells
/// how its flight met each unit and ended (#479): a hit or a pass-through before the damage it deals, then where it
/// expired or was blocked.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pooled (#1181 Part D).</b> Each instance's cast system takes its projectiles from its
/// <see cref="AbilityScriptPool" />, so a warm projectile cast allocates nothing. A projectile from the pool lives cast
/// after cast: <see cref="PooledAbilityScript.Launch" /> binds it to the next (its own copy of the caster's ability made
/// again, the caster and the aim) and leaves every other field as this constructor would: no hit set, nothing travelled,
/// at rest at the origin, cast id 0, executing, nothing unsent. <see cref="PooledAbilityScript.Clear" /> lets go of its
/// cast when it goes back: its copy cleared, no caster, its hit set released, finished, so a pooled projectile keeps
/// nothing alive. Built through the public constructor (the activator, a test), it is an ordinary projectile no pool takes.
/// </para>
/// <para>
/// <b>Its guid.</b> One wrapper of its own, made at its first <see cref="Prepare" />, which writes a new id into it at
/// every flight: an id is never reused, the wrapper is. The pool hands a projectile out again only once no view can still
/// hold its wrapper for the flight before (two view rounds, <see cref="AbilityScriptPool" />).
/// </para>
/// <para>
/// <b>Its state</b> is one of two boxes every projectile shares (<c>s_executing</c>, <c>s_finished</c>), since
/// <see cref="State" /> is an object: a flight boxes nothing. <c>State is SpellState.Finished</c> reads them as any box.
/// </para>
/// <para>
/// <b>A fan (#1181 Part D, <c>AddedProjectiles</c>).</b> The cast system fires each extra lane of a fan as a projectile
/// of its own and joins it to the cast's first (<see cref="IProjectileFan.ShareHitsWith" />) before it is prepared, so
/// every lane of the cast shares the first's <see cref="ProjectileHits" />, one count more per lane, and makes no set of
/// its own: a unit is affected at most once per cast, whichever lane reaches it first, and the set goes back to the pool
/// when the last lane lets go of it. Each lane counts its own hits (<c>_struck</c>) for <c>MaxTargets</c>, so a piercing
/// lane never ends on another lane's hits.
/// </para>
/// <para>
/// <b>An explosion (#1181 Part D, <c>ImpactExplosion</c>).</b> When the cast's <c>ExplosionRadius</c> is above 0 and the
/// projectile ends on a unit, a circle of that radius bursts where it met the unit (<see cref="Explode" />), told as a
/// fired circle, the event clients already draw. It hits every other unit in it the ability may affect and the cast has
/// not hit yet, each once and added to the cast's set, at <c>ExplosionEffectiveness</c> % of the amount with the
/// <c>Area</c> tag: damage only, no aura, no second hit, no dodge wait. Never at a wall or at full reach. It runs inside
/// the flight's rewound view, as the sweep does.
/// </para>
/// </remarks>
public sealed class ProjectileAbilityScript : PooledAbilityScript, IProjectileIdentity, IProjectileFan
{
    /// <summary>Where the projectile is drawn above the ground it flies along. Hit tests ignore height.</summary>
    public static readonly Vector3 HeightOffset = new(0f, 0.5f, 0f);

    private const float Epsilon = 0.001f;

    private static readonly object s_executing = SpellState.Executing;
    private static readonly object s_finished = SpellState.Finished;

    private readonly IAbilityArena _arena;

    // The units this cast has hit, every lane of a fan's: taken in Prepare, from the pool for a pooled projectile, unless
    // a fan's first lane shared its own (ShareHitsWith), and let go by Clear.
    private ProjectileHits? _hits;

    // The units this lane has hit, for MaxTargets: a fan's lanes share the set, never the count.
    private int _struck;

    // The wrapper this projectile's ids are written into, its own: Guid is set to it, and only it is ever changed here.
    private ObjectGuid? _wrapper;
    private Vector3 _ground;
    private Vector3 _direction;
    private float _travelled;

    public ProjectileAbilityScript(IAbility ability, IUnit caster, AbilityAim aim, IAbilityArena arena)
        : base(ability, caster, aim)
    {
        _arena = arena;
    }

    public override Vector3 Position { get; set; }
    public override Vector3 Velocity { get; set; }
    public override Vector3 Orientation { get; set; }
    public override ObjectGuid Guid { get; set; }
    public override object State { get; set; } = s_executing;

    public uint AbilityId => Ability.AbilityId.Value;
    public ulong CasterGuid => Caster.Guid.RawValue;
    public uint CastId { get; private set; }

    protected override bool ShouldRun() => true;

    private protected override void OnLaunch()
    {
        _hits = null;
        _struck = 0;
        _ground = Vector3.zero;
        _direction = Vector3.zero;
        _travelled = 0f;
        Position = Vector3.zero;
        Velocity = Vector3.zero;
        Orientation = Vector3.zero;
        CastId = 0;
        State = s_executing;
    }

    private protected override void OnClear()
    {
        if (_hits is { } hits)
        {
            _hits = null;
            Pool?.Release(hits);
        }

        State = s_finished;
    }

    public override void Prepare()
    {
        IAbilityArena arena = _arena;
        _wrapper ??= new ObjectGuid();
        _wrapper.Set(ObjectType.SpellProjectile, IObject.GenerateId());
        Guid = _wrapper;
        // A lane of a fan already shares its first lane's set. One built outside a pool counts its own lane, never let go.
        _hits ??= Pool?.TakeHits() ?? new ProjectileHits { Lanes = 1 };
        CastId = arena.CastInFlight;
        _ground = Origin;
        _direction = Aim.DirectionFrom(_ground);
        Velocity = _direction * Ability.Metadata.ProjectileSpeed;
        Orientation = new Vector3(0f, MathF.Atan2(_direction.x, _direction.z) * Mathf.Rad2Deg, 0f);
        Position = _ground + HeightOffset;

        // A queued cast's lane was resolved for its start broadcast; an instant's is resolved here.
        AbilityFootprint? lane = arena.FootprintInFlight is { Shape: AbilityShape.Projectile } started
            ? started
            : AbilityFootprint.Resolve(AbilityShape.Projectile, Ability.Metadata, Aim, _ground,
                arena.GetNavigatorForPosition(_ground));
        if (lane is { } fired)
        {
            arena.BroadcastAbilityFired(Caster, Ability, fired);
        }
    }

    public override void Update(TimeSpan deltaTime)
    {
        if (State is SpellState.Finished)
        {
            return;
        }

        IAbilityArena arena = _arena;
        HashSet<ObjectGuid> hit = _hits!.Units;
        AbilityMetadata meta = Ability.Metadata;
        float step = Math.Min(meta.ProjectileSpeed * (float)deltaTime.TotalSeconds, meta.Reach - _travelled);
        step = Math.Max(step, 0f);

        Vector3 wanted = new(_ground.x + _direction.x * step, _ground.y, _ground.z + _direction.z * step);
        Vector3 end = step > 0f ? arena.GetNavigatorForPosition(_ground).RaycastWalkable(_ground, wanted) : _ground;
        float moved = HitShapes.Distance2D(_ground, end);
        bool blocked = moved < step - Epsilon;

        // By index: a foreach through the interface boxes the list's enumerator on every step of the flight (#880).
        IReadOnlyList<IUnit> units = arena.Hits.OnSegment(_ground, end);
        for (int i = 0; i < units.Count; i++)
        {
            IUnit unit = units[i];
            if (hit.Contains(unit.Guid) || !AbilityEffect.Qualifies(arena, Caster, Ability, unit))
            {
                continue;
            }

            hit.Add(unit.Guid);
            _struck++;

            // Without Pierce the first hit is the last; with it, this lane's MaxTargets-th (#1181), told as a Hit.
            bool last = !meta.Pierce || (meta.MaxTargets > 0 && _struck >= meta.MaxTargets);

            // Where it met the unit: the point of its step nearest the unit's centre, where the sweep tested it
            // (rewound for a player's projectile, #1082). Told before the damage, so the damage belongs to it.
            Vector3 hitPoint = HitShapes.ClosestPointOnSegment(_ground, end, arena.Hits.PositionOf(unit));
            arena.BroadcastProjectileImpact(Guid, Caster, Ability, CastId, hitPoint + HeightOffset,
                last ? ProjectileImpactReason.Hit : ProjectileImpactReason.Pierced, unit);
            AbilityEffect.Apply(arena, Caster, Ability, unit);

            if (last)
            {
                // Ended on a unit: the explosion bursts there. The list it walks is the one this loop read, reused by the
                // query, so nothing reads `units` after it.
                if (meta.ExplosionRadius > 0f)
                    Explode(hitPoint, unit);

                MoveTo(hitPoint, HitShapes.Distance2D(_ground, hitPoint));
                Finish();
                return;
            }
        }

        MoveTo(end, moved);

        if (blocked || _travelled >= meta.Reach - Epsilon)
        {
            arena.BroadcastProjectileImpact(Guid, Caster, Ability, CastId, Position,
                blocked ? ProjectileImpactReason.Blocked : ProjectileImpactReason.Expired, null);
            Finish();
        }
    }

    void IProjectileFan.ShareHitsWith(IProjectileFan first)
    {
        ProjectileHits hits = ((ProjectileAbilityScript)first)._hits
                              ?? throw new InvalidOperationException("A fan's first lane shares its hits once it is prepared.");
        hits.Lanes++;
        _hits = hits;
    }

    /// <summary>
    /// The explosion where the projectile ended on <paramref name="struck" /> (#1181 Part D): told as a fired circle of the
    /// cast's <c>ExplosionRadius</c> at <paramref name="at" />, under the cast's id, then every unit in it but the struck
    /// one, any the cast already hit and any the ability may not affect is hit once, at <c>ExplosionEffectiveness</c> % with
    /// the Area tag, and added to the cast's set. By index: the query's list is reused, and a foreach through the
    /// interface would box its enumerator.
    /// </summary>
    private void Explode(Vector3 at, IUnit struck)
    {
        IAbilityArena arena = _arena;
        AbilityMetadata meta = Ability.Metadata;
        float radius = meta.ExplosionRadius;

        // The fired event names the projectile's cast, as its lane's did, so a client ties the burst to it.
        uint outerCast = arena.CastInFlight;
        try
        {
            arena.CastInFlight = CastId;
            arena.BroadcastAbilityFired(Caster, Ability,
                new AbilityFootprint(AbilityShape.Circle, at, null, at, radius, 0f, 0f));
        }
        finally
        {
            arena.CastInFlight = outerCast;
        }

        HashSet<ObjectGuid> hit = _hits!.Units;
        var shaping = new HitShaping(meta.ExplosionEffectiveness, AbilityTags.Area);
        IReadOnlyList<IUnit> units = arena.Hits.InCircle(at, radius);
        for (int i = 0; i < units.Count; i++)
        {
            IUnit unit = units[i];
            if (ReferenceEquals(unit, struck) || hit.Contains(unit.Guid)
                                              || !AbilityEffect.Qualifies(arena, Caster, Ability, unit))
            {
                continue;
            }

            hit.Add(unit.Guid);
            arena.HitForAbility(Caster, unit, Ability, shaping);
        }
    }

    private void MoveTo(Vector3 ground, float moved)
    {
        _ground = ground;
        _travelled += moved;
        Position = _ground + HeightOffset;
        _dirtyFields |= GameEntityFields.Position;
    }

    private void Finish()
    {
        State = s_finished;
        Velocity = Vector3.zero;
        _dirtyFields |= GameEntityFields.Velocity;
    }
}
