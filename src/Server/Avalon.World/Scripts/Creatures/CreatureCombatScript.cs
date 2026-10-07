using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.State;
using Avalon.World.Abilities;
using Avalon.World.Abilities.Targeting;
using Avalon.World.Creatures;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Creatures;

/// <summary>
/// Fights a target: chases it onto a melee slot, leashes home past the chase distance, gives up on a target it
/// cannot reach, and attacks with the creature's abilities (#163). Which ability, and when, is
/// <see cref="ChooseAbility" />: the base casts the basic whenever it is ready and the target is in its reach,
/// and each creature type's own script overrides it with its rotation. The creature's abilities come from the
/// kit its script passes to the protected constructor; the public one, which data can name, has none, and a
/// creature on it chases but never attacks.
/// </summary>
/// <remarks>
/// An instant ability fires through <c>RunInstantAbility</c>. One with a cast time is a wind-up: it is queued
/// with the aim captured now, the creature stops, and it asks for no movement until the cast ends (it fires,
/// or the cast system drops it because the creature died or turned for home), so the cast is never
/// interrupted by its own steps and a player can step out of it.
/// </remarks>
public class CreatureCombatScript : AiScript, IReturningHome
{
    /// <summary>
    /// Chasing the target is part of <see cref="Combat" />; there is no separate chase state (#598).
    /// </summary>
    public enum CombatState
    {
        None,
        Combat,
        Returning
    }

    // This is the position distance at which the creature will stop chasing the target if no hits were received in the meantime, and if the creature itself didn't hit the target
    private const float MaxChaseDistance = 40.0f;

    // Internal rather than private so MapInstance's constructor can warn when a configured
    // MeleeSlotRadius exceeds what this script can actually reach (see MapInstance.cs, near where
    // it reads world.Configuration.MeleeSlotRadius) without duplicating this balance constant.
    internal const float AttackRange = 1.5f;

    // Slightly inside AttackRange rather than exactly on it, matching how these creatures behaved
    // before this feature existed: walking straight at the target and stopping the instant they
    // first crossed AttackRange left them strictly inside it, never balanced exactly on the
    // comparison boundary the in-range check below tests against. A destination sitting exactly
    // on that boundary is asking for float noise to decide it, regardless of what margin is or
    // isn't applied around the check.
    private const float SurplusStandOffInset = 0.1f;

    // Two uses, both pre-existing, both measured in "how far may the target wander before the
    // creature's current plan is stale enough to redo":
    //
    // 1. How far a settled surplus (no-slot) creature's target may wander before its destination
    //    (a stand-off point at AttackRange from the target, on this creature's own bearing — see
    //    ChooseDestination) counts as drifted enough to re-engage movement. A settled *slotted* creature uses
    //    Context.Locomotion.ArrivalTolerance instead: see KeepStation.
    // 2. How far the target may move away from where it was standing when the path currently being
    //    walked was planned (_lastRequestedDestination) before that path is re-planned mid-walk. This
    //    is the value that shipped before the locomotion seam existed, applied to exactly the same
    //    quantity it was applied to then, so the FindPath rate this bounds is the pre-branch one:
    //    at most one query per 1.5 units of target movement, never per tick.
    //
    // Deliberately NOT a smaller number, and deliberately not reused as a dead-band on the settled
    // drift check: fix round 3 tried a 0.4 dead-band added to the *settled* slotted threshold and it
    // stranded creatures — a settled creature's slack above AttackRange is only
    // ArrivalTolerance + AttackRangeArrivalMargin (~0.15 under Waypoint), so any dead-band large
    // enough to matter drops the creature out of attack range before it re-paths. That hazard is
    // specific to the settled check; a creature that is still walking is not attacking anything
    // yet, so there is no attack window for a mid-walk threshold to fall outside of.
    private const float PathRecalculationThreshold = 1.5f;

    // Gated on locomotion reporting arrival (see hasArrived in Engage) — not unconditional, and
    // not also on hasSlot. An unconditional allowance changes the effective attack range for
    // every creature, including one still approaching from far away, and at CrowdLocomotion's
    // maximum configured agent radius (10) that is AttackRange + 10 + 0.05 = 11.55: a de facto
    // balance change to AttackRange itself, which this must never be (see
    // MapInstance/GameConfiguration for the equally deliberate constraint that MeleeSlotRadius
    // must not exceed AttackRange plus this same tolerance, for the same reason in reverse).
    // Gating on arrival alone closes that off: an approaching creature (locomotion still has a
    // path queued) gets none of it, so this never widens the range for something still closing
    // distance — only for something that has already stopped moving. hasSlot must NOT be part of
    // that gate, though: the margin rescues ANY creature that has settled from the residual gap
    // its locomotion's own arrival epsilon may have left it with, and that applies exactly as
    // much to a surplus creature settling at its stand-off point (see destination above) as to a
    // slotted one settling at its ring position — both sit at (or, for the surplus case, just
    // inside) AttackRange by construction, so both are exposed to the identical boundary hazard.
    // Once a creature has settled, it never retries to close that residual gap on its own — see
    // the movement half of the tick (KeepStation), which only re-engages once the gap exceeds this SAME
    // tolerance, not to shrink it further — so without the margin a creature that happened to
    // settle a hair past AttackRange would be stuck there, forever, attacking nothing. The margin
    // below is that same locomotion-sourced tolerance (see the note on
    // Context.Locomotion.ArrivalTolerance for why it has to come from there rather than a
    // constant here) plus a small fixed buffer against float noise in the distance comparison
    // itself.
    private const float AttackRangeArrivalMargin = 0.05f;

    // How long a creature may go without any way to reach its target before it gives up and goes
    // home, as it does past the leash (#606). "No way to reach" is decided from where the creature's
    // route really ends (ICreatureLocomotion.ResolvedDestination), never from HasArrived: a partial
    // route toward a target on a ledge or an island is walked like any other, so arrival flips on and
    // off while the creature stands at its end. See RouteCanReach. Only an unbroken stretch counts;
    // a tick in range or with a route that can reach starts the count over, as does a new target.
    private static readonly TimeSpan s_unreachableGiveUpTime = TimeSpan.FromSeconds(5);

    // The safety net on the walk home (#715): a creature walking home ignores every hit (#610), so one that
    // never counts as home would be unhittable for good. It is put home and reset once it has come no closer
    // to home, by ReturnProgressStep on X/Z, for s_returnStallLimit, or has been on its way for s_returnHomeLimit.
    // The leash is 40 m, 10 s at the slowest seeded run speed (4 m/s), so the cap leaves room for a detour.
    private static readonly TimeSpan s_returnStallLimit = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan s_returnHomeLimit = TimeSpan.FromSeconds(15);
    private const float ReturnProgressStep = 0.5f;

    private readonly ILogger<CreatureCombatScript> _logger;
    private readonly TimeProvider _time;
    private readonly CreatureAbilities _abilities;

    private bool _dead;

    // Where the fight began, and where the creature goes back to. Null while no fight has set it: any
    // position is a valid home, the origin included (#606).
    private Vector3? _home;

    // How long, without a break, the creature has had no way to reach its target (#606).
    private TimeSpan _unreachableFor;

    // The walk home's safety net (#715): how long it has been walking home, how long since it last came
    // ReturnProgressStep closer, and the closest to home (X/Z) it has been. Set by BeginReturn.
    private TimeSpan _returningFor;
    private TimeSpan _returnStalledFor;
    private float _closestToHome = float.MaxValue;

    // The destination the journey now in progress was planned for. This is what the mid-walk
    // staleness check in KeepStation measures against, and it answers a different question from the
    // settled check next to it: "is where I am headed still where I should be headed" versus "have
    // I stopped somewhere that is no longer good enough". Written only by RequestMoveTo, which is
    // the single route this script has to the locomotion's MoveTo — so there is no way to start a
    // journey without recording what it was planned for.
    private Vector3 _lastRequestedDestination;

    private IUnit? _target;

    /// <summary>
    /// A combat script with no abilities: the creature chases, leashes and goes home, but attacks nothing (#163).
    /// A creature type's own script passes its kit to the protected constructor.
    /// </summary>
    /// <param name="time">The container's clock, the one the rest of the world times by (#610).</param>
    public CreatureCombatScript(ILoggerFactory loggerFactory, ICreature creature, ISimulationContext context,
        TimeProvider? time = null)
        : this(loggerFactory, creature, context, time, catalog: null, kit: null)
    {
    }

    /// <summary>
    /// A combat script that fights with <paramref name="kit" /> (#163), loaded now from
    /// <paramref name="catalog" /> into the creature's own abilities: an id the catalog does not hold is
    /// logged and left out. A creature that is not the World-side type (a test substitute; the modding API
    /// cannot make creatures) keeps them on this script instead.
    /// </summary>
    /// <param name="catalog">The current ability catalog; null when none is loaded, and the creature gets none.</param>
    protected CreatureCombatScript(ILoggerFactory loggerFactory, ICreature creature, ISimulationContext context,
        TimeProvider? time, AbilityCatalog? catalog, CreatureAbilityKit? kit) : base(creature, context)
    {
        _logger = loggerFactory.CreateLogger<CreatureCombatScript>();
        _time = time ?? TimeProvider.System;
        _abilities = creature is Avalon.World.Entities.Creature worldCreature
            ? worldCreature.Abilities
            : new CreatureAbilities();

        if (kit is null)
        {
            return;
        }

        if (catalog is null)
        {
            _logger.LogWarning("No ability catalog is loaded; creature {CreatureName} fights without abilities",
                creature.Name);
            return;
        }

        _abilities.Load(catalog, kit, _logger, creature.Name);
    }

    /// <summary>The creature's abilities, as its kit loaded them (#163).</summary>
    protected CreatureAbilities Abilities => _abilities;

    /// <summary>Whom the creature is fighting, or null.</summary>
    protected IUnit? Target => _target;

    /// <summary>Where the creature goes back to. Every path into Combat sets <see cref="_home" /> first.</summary>
    private Vector3 Home => _home ?? Creature.Position;

    public override object State { get; set; } = CombatState.None;

    bool IReturningHome.IsReturningHome => State is CombatState.Returning;

    /// <summary>
    /// Its target left this creature's instance: give up the fight and go home, at full health. Called
    /// by the instance for its own creatures only (#546), so nothing outside it holds this script.
    /// </summary>
    public override void OnCharacterLeft(ICharacter character)
    {
        base.OnCharacterLeft(character);

        if (_target == character && !_dead)
        {
            // Release before nulling _target — Release needs the target's guid.
            Context.MeleeSlots.Release(_target.Guid, Creature.Guid);
            _target = null;
            BeginReturn();
            Creature.CurrentHealth = Creature.Health;
            _unreachableFor = TimeSpan.Zero;
            RequestMoveTo(Home);
        }
    }

    protected override bool ShouldRun() => State is CombatState.Combat or CombatState.Returning;

    public override void OnEnteredRange(ICharacter character)
    {
        if (State is CombatState.None)
        {
            _target = character;
            _home = Creature.Position;
            _unreachableFor = TimeSpan.Zero;
            State = CombatState.Combat;
        }
    }

    public override void OnHit(IUnit attacker, uint damage)
    {
        if (State is not CombatState.Returning)
        {
            // Health is a uint: a hit of at least what is left kills, rather than wrapping (#588).
            if (damage >= Creature.CurrentHealth)
            {
                _logger.LogInformation("{Name} has died", Creature.Name);
                uint taken = Creature.CurrentHealth;
                Creature.CurrentHealth = 0;
                _dead = true;

                // The killing blow is sent like any other hit (#506 review), for the health it took, so
                // its crit or block is seen; the death broadcast follows once this hit returns.
                Context.BroadcastUnitHit(attacker, Creature, 0, taken);

                // _dead short-circuits Update from here on, so this is the script's only chance
                // to give back whatever slot it held.
                // The kill itself (loot, experience, the corpse) is the combat service's to report to
                // the instance, once this hit returns (#546).
                if (_target is not null)
                    Context.MeleeSlots.Release(_target.Guid, Creature.Guid);

                return;
            }

            Creature.CurrentHealth -= damage;
            Engage(attacker);
            Context.BroadcastUnitHit(attacker, Creature, Creature.CurrentHealth, damage);
        }
    }

    /// <summary>
    /// A dodged hit (#506 review) engages the creature against its attacker exactly as a landed one does,
    /// with no damage and no broadcast (the combat service sends the dodge). Without it, an opening hit
    /// that is dodged from beyond detection range would leave the creature idle while the encounter held
    /// its threat. Ignored on the walk home and by a corpse, as a hit is.
    /// </summary>
    public override void OnAttacked(IUnit attacker)
    {
        base.OnAttacked(attacker);

        if (State is not CombatState.Returning && !_dead)
            Engage(attacker);
    }

    /// <summary>
    /// Fight <paramref name="attacker" />: a different unit switches target immediately, same as the
    /// top-threat reconciliation in Update, releasing whatever slot was held on the old one first.
    /// </summary>
    private void Engage(IUnit attacker)
    {
        if (!ReferenceEquals(_target, attacker))
        {
            if (_target is not null)
                Context.MeleeSlots.Release(_target.Guid, Creature.Guid);

            _unreachableFor = TimeSpan.Zero;
        }

        _target = attacker;
        _home ??= Creature.Position;

        State = CombatState.Combat;
    }

    public override void Update(TimeSpan deltaTime)
    {
        // #163: the creature's cooldowns run down here, the one place they are cast from, first so this tick
        // sees them. They matter only while this script fights, and ResetToIdleAtSpawn clears them.
        _abilities.Update(deltaTime);

        if (_dead)
        {
            return;
        }

        Vector3 currentPosition = Creature.Position;

        // Reconcile current target with the encounter's authoritative threat list (and any
        // active taunt). The local _target field still seeds initial engagement (set by
        // OnEnteredRange / OnHit) and survives ticks when no encounter is active, but as
        // soon as CombatService spawns or updates an encounter for this creature, the
        // top-threat attacker (or the taunter, while the taunt is active) becomes the
        // authoritative pick. Only switch targets while actively engaging — Returning is
        // handled by its own block below.
        if (State is CombatState.Combat)
        {
            ReconcileTarget();
        }

        if (State is CombatState.Returning)
        {
            UpdateReturning(currentPosition, deltaTime);
            return;
        }

        if (_target == null)
        {
            return;
        }

        // Disengage if the target became dead (player hit 0 HP and is in respawn modal,
        // or another mob landed the killing blow). Without this the creature keeps grinding
        // attacks against the corpse — visible client-side as repeated hit packets after
        // the death overlay shows up. Drop target + return to spawn.
        if (_target is ICharacter targetChar && targetChar.IsDead)
        {
            GiveUpAndGoHome(_target);
            return;
        }

        Vector3 targetPosition = _target.Position;

        if (Vector3.Distance(currentPosition, Home) > MaxChaseDistance)
        {
            GiveUpAndGoHome(_target);
            return;
        }

        // Winding up (#163): the creature stands where the cast began and asks for no movement until the
        // cast ends, so its own steps never interrupt it. The unreachable count does not run while it stands.
        if (_abilities.IsCasting)
        {
            _unreachableFor = TimeSpan.Zero;
            return;
        }

        Engage(_target, currentPosition, targetPosition, deltaTime);

        if (_unreachableFor > s_unreachableGiveUpTime)
        {
            GiveUpAndGoHome(_target);
        }
    }

    /// <summary>
    /// Switches to the authoritative pick (see <see cref="PickTarget" />) when there is one and it is
    /// not the current target: the slot held on the old target is given back and the journey toward
    /// it is stopped.
    /// </summary>
    private void ReconcileTarget()
    {
        IUnit? picked = PickTarget();
        if (picked is not null && !ReferenceEquals(picked, _target))
        {
            if (_target is not null)
                Context.MeleeSlots.Release(_target.Guid, Creature.Guid);

            _target = picked;
            _unreachableFor = TimeSpan.Zero;
            Context.Locomotion.Stop(Creature);
        }
    }

    /// <summary>
    /// The Returning state's tick: rest once home, otherwise keep running there, asking for a new route
    /// when the last one ended, snapping home when there is none or it ends short of home, and snapping
    /// home when the walk makes no progress or takes too long (#715).
    /// </summary>
    /// <remarks>
    /// Home is where the fight began, often the creature's spawn, which need not lie on the navmesh: a
    /// procedural spawn keeps its slot's height, and a spawn near a wall sits in the mesh's eroded border. A
    /// route home therefore ends at the nearest point the mesh has (a navmesh route's points carry the mesh's
    /// own height), and a crowd stops within its arrival tolerance of that. Home is reached once the
    /// locomotion has nothing left to walk and the creature stands within that tolerance of home on X/Z;
    /// comparing its exact position with home, as before #715, never held for such a home, and the creature
    /// re-planned the same route for ever, walking home and ignoring every hit.
    /// </remarks>
    private void UpdateReturning(Vector3 currentPosition, TimeSpan deltaTime)
    {
        ICreatureLocomotion locomotion = Context.Locomotion;
        float tolerance = locomotion.ArrivalTolerance(Creature) + AttackRangeArrivalMargin;
        float fromHome = HitShapes.Distance2D(currentPosition, Home);

        if (Vector3.Distance(currentPosition, Home) < 0.1f
            || (fromHome <= tolerance && locomotion.HasArrived(Creature)))
        {
            ResetToIdleAtSpawn();
            return;
        }

        // Standing at the end of a route home that ends short of it: home is farther off the mesh than the
        // tolerance, or the mesh does not connect to it. A crowd never reports such a partial route as arrived,
        // so the route's end is judged, not HasArrived.
        if (locomotion.ResolvedDestination(Creature) is { } routeEnd
            && HitShapes.Distance2D(routeEnd, Home) > tolerance
            && Vector3.Distance(currentPosition, routeEnd) <= tolerance)
        {
            SnapHome("its route home ends short of home");
            return;
        }

        // The journey may be over because MoveTo at transition-time failed (DotRecast returned no route:
        // start and end on disconnected polygons). Without a new request the creature drifts: the server
        // keeps Position static but MoveState=Running and a stale Velocity, so the client extrapolates
        // indefinitely.
        if (locomotion.HasArrived(Creature))
        {
            RequestMoveTo(Home);
            if (locomotion.HasArrived(Creature))
            {
                SnapHome("no route home exists");
                return;
            }
        }

        // The safety net (#715): whatever keeps a creature from getting home, it never stays returning, and so
        // unhittable, for good.
        _returningFor += deltaTime;
        if (fromHome < _closestToHome - ReturnProgressStep)
        {
            _closestToHome = fromHome;
            _returnStalledFor = TimeSpan.Zero;
        }
        else
        {
            _returnStalledFor += deltaTime;
        }

        if (_returnStalledFor > s_returnStallLimit)
        {
            SnapHome($"it came no closer to home for {s_returnStallLimit.TotalSeconds:0} s");
            return;
        }

        if (_returningFor > s_returnHomeLimit)
        {
            SnapHome($"it was still on its way home after {s_returnHomeLimit.TotalSeconds:0} s");
            return;
        }

        Creature.MoveState = MoveState.Running;
        Creature.Speed = Creature.Metadata.SpeedRun;
    }

    /// <summary>
    /// Puts the creature home through the locomotion and resets it there, exactly as if it had walked home
    /// (#715). Home rather than where it stands: it is a place the creature stood, and a creature left wherever
    /// it got stuck would drift a little farther from its spawn with every kite. Logged once, at Warning, since
    /// the reset ends the return.
    /// </summary>
    private void SnapHome(string why)
    {
        Vector3 home = Home;
        _logger.LogWarning(
            "Creature {CreatureName} ({CreatureGuid}) could not walk home from {Position} to {Home}: {Reason}; put it home and reset it",
            Creature.Name, Creature.Guid, Creature.Position, home, why);
        Context.Locomotion.Teleport(Creature, home);
        ResetToIdleAtSpawn();
    }

    /// <summary>Starts the walk home: the safety net's clocks start over, measured from where it stands.</summary>
    private void BeginReturn()
    {
        State = CombatState.Returning;
        _returningFor = TimeSpan.Zero;
        _returnStalledFor = TimeSpan.Zero;
        _closestToHome = HitShapes.Distance2D(Creature.Position, Home);
    }

    /// <summary>
    /// Drops <paramref name="target" /> and heads home at full health: the target died, the creature
    /// was drawn past <see cref="MaxChaseDistance" /> from where the fight began, or it had no way to
    /// reach the target for longer than <see cref="s_unreachableGiveUpTime" /> (#606).
    /// </summary>
    private void GiveUpAndGoHome(IUnit target)
    {
        Context.MeleeSlots.Release(target.Guid, Creature.Guid);
        _target = null;
        _unreachableFor = TimeSpan.Zero;
        BeginReturn();
        RequestMoveTo(Home);
        Creature.CurrentHealth = Creature.Health;
    }

    /// <summary>
    /// One tick of fighting <paramref name="target" />: pick where to stand, attack when an ability is ready
    /// and in reach, and keep walking to where it should stand, in that order. A wind-up that begins this
    /// tick ends it: the creature stands for the cast.
    /// </summary>
    private void Engage(IUnit target, Vector3 currentPosition, Vector3 targetPosition, TimeSpan deltaTime)
    {
        bool hasSlot = ChooseDestination(target, currentPosition, targetPosition, out Vector3 destination);

        bool hasArrived = Context.Locomotion.HasArrived(Creature);

        // Attacking and keeping station are independent: a creature can attack the target the instant an
        // ability reaches it, in the very same tick it is also stepping to keep pace with a target (and so
        // a slot) that is on the move — Stop is never called merely for being in range (see KeepStation).
        // Where it stands is still decided by AttackRange: the allowance beyond it is gated on arrival alone
        // (see AttackRangeArrivalMargin's comment for the full reasoning, including why hasSlot is
        // deliberately NOT part of this gate).
        float effectiveAttackRange = hasArrived
            ? AttackRange + Context.Locomotion.ArrivalTolerance(Creature) + AttackRangeArrivalMargin
            : AttackRange;

        bool inRange = Vector3.Distance(currentPosition, targetPosition) <= effectiveAttackRange;
        if (inRange)
        {
            Creature.LookAt(targetPosition);
        }

        if (TryAttack(target, currentPosition, targetPosition, facing: inRange))
        {
            _unreachableFor = TimeSpan.Zero;
            return;
        }

        // No way to reach the target (#606). Read before KeepStation asks for a new route, so both
        // locomotions answer for a route they have already planned: a crowd plans a new request only
        // on its next Update.
        _unreachableFor = !inRange && !RouteCanReach(targetPosition)
            ? _unreachableFor + deltaTime
            : TimeSpan.Zero;

        KeepStation(currentPosition, destination, hasSlot, hasArrived, inRange);
    }

    /// <summary>
    /// Whether the creature's current route can bring it within reach of <paramref name="targetPosition" />
    /// (#606). It can when the route ends within attack range, plus the locomotion's arrival tolerance and
    /// the margin, of the target, measured from the target rather than the slot: a slot against a wall
    /// that the route cannot reach, but whose route ends within reach of the target, is reachable. No
    /// route, or a partial one toward a ledge or an island, is not. A settled creature out of range
    /// re-plans every tick (see KeepStation), so a stale route is never what this judges for long.
    /// </summary>
    private bool RouteCanReach(Vector3 targetPosition)
    {
        if (Context.Locomotion.ResolvedDestination(Creature) is not { } routeEnd)
        {
            return false;
        }

        float tolerance = Context.Locomotion.ArrivalTolerance(Creature) + AttackRangeArrivalMargin;
        return Vector3.Distance(routeEnd, targetPosition) <= AttackRange + tolerance;
    }

    /// <summary>
    /// Where this creature should stand to fight <paramref name="target" />. Returns whether it holds a
    /// melee slot on the target.
    /// </summary>
    private bool ChooseDestination(IUnit target, Vector3 currentPosition, Vector3 targetPosition, out Vector3 destination)
    {
        // Destination: the claimed slot's position when a slot is held, otherwise a stand-off
        // point just inside AttackRange from the target along this creature's own current
        // bearing — never the target's exact centre. A surplus creature (ring full) was always
        // meant to degrade to today's behaviour, piling up near AttackRange same as before this
        // feature existed, not to walk inside the player: removing Stop from the in-range branch
        // (so a creature can keep adjusting footing while also attacking) took away the only
        // thing that used to hold a no-slot creature off its target. Vector3.Normalize returns
        // zero for a near-zero input rather than NaN, so a creature that somehow ends up exactly
        // on the target's position degrades to standing on the target rather than throwing — a
        // pathological case, not one this needs to solve.
        //
        // Computed once per tick so movement only pays for one TryClaim per tick (idempotent —
        // see MeleeSlots.TryClaim — but there is no reason to call it twice).
        bool hasSlot = Context.MeleeSlots.TryClaim(target.Guid, Creature.Guid, targetPosition, currentPosition, out int slot);
        destination = hasSlot
            ? Context.MeleeSlots.PositionFor(targetPosition, slot)
            : targetPosition + Vector3.Normalize(currentPosition - targetPosition) * (AttackRange - SurplusStandOffInset);
        destination = WithinBasicReach(target, targetPosition, destination);
        return hasSlot;
    }

    /// <summary>
    /// <paramref name="destination" />, pulled in toward the target along its own bearing when needed so the
    /// basic reaches the target's body from anywhere within the locomotion's arrival tolerance of it (#163). A
    /// crowd counts a creature arrived up to its agent radius off its slot, and a short basic (the Blightfly's
    /// 1.5 m Sting) would otherwise miss from there for good: settled within tolerance, it never re-plans. With
    /// the default slot radius and waypoint locomotion nothing moves.
    /// </summary>
    private Vector3 WithinBasicReach(IUnit target, Vector3 targetPosition, Vector3 destination)
    {
        if (_abilities.Basic is not { } basic)
        {
            return destination;
        }

        float reach = basic.Metadata.Shape == AbilityShape.Circle && basic.Metadata.Anchor == AbilityAnchor.Caster
            ? basic.Metadata.Radius
            : basic.Metadata.Reach;
        float farthest = reach + target.BodyRadius - Context.Locomotion.ArrivalTolerance(Creature) - SurplusStandOffInset;
        float standing = HitShapes.Distance2D(targetPosition, destination);
        if (farthest <= 0f || standing <= farthest)
        {
            return destination;
        }

        Vector3 offset = destination - targetPosition;
        return targetPosition + new Vector3(offset.x, 0f, offset.z) * (farthest / standing);
    }

    /// <summary>
    /// Hands the locomotion a fresh destination when the current one has gone stale, and keeps the
    /// creature running whenever it should be moving at all.
    /// </summary>
    private void KeepStation(Vector3 currentPosition, Vector3 destination, bool hasSlot, bool hasArrived, bool inRange)
    {
        // Two independent reasons to hand the locomotion a fresh destination, because a creature
        // that is walking and a creature that has settled go stale in different ways:
        //
        // 1. settledOffDestination — it has arrived (locomotion has nothing left to walk towards)
        //    but the destination has since moved away from where it is standing by more than its
        //    tolerance. This is what makes a moving target's slot get chased rather than abandoned:
        //    locomotion has no idea the target moved once it has gone idle, so without it a creature
        //    that settled at its slot would stay there forever as the target (and so the slot) walks
        //    away. A slotted creature uses the locomotion's own arrival tolerance for "how far is
        //    too far to ignore" — the same idea as PathRecalculationThreshold, applied to the slot
        //    instead of the raw target position, and reusing ArrivalTolerance rather than a second
        //    invented constant since it already answers exactly "how close counts as close enough"
        //    for this locomotion. A surplus (no-slot) creature uses PathRecalculationThreshold.
        //
        // 2. destinationDrifted — it is still walking, but where it should be headed has moved more
        //    than PathRecalculationThreshold from the destination the journey in progress was
        //    planned for, so the path leads somewhere stale. For a slotted chaser that is the target
        //    having moved, one-for-one: a slot's position is a fixed offset from the target, so the
        //    destination drifts exactly as far as the target does — this is the pre-seam re-path
        //    condition, expressed against the destination so that a journey planned for something
        //    else entirely (a leash-return home interrupted by a fresh hit) also counts as stale
        //    without a separate flag to keep in sync. Without this a creature commits to
        //    its destination for the whole journey: MapNavigator emits a waypoint every 0.5 units,
        //    so a 10-unit approach is ~2.5s at SpeedRun 4 of running at where the target used to be
        //    — a player who changes direction mid-pull is simply not followed until the creature
        //    finishes walking to the old spot. This is what the pre-seam script did (it re-pathed on
        //    exactly this quantity, at exactly this threshold, whether or not it had arrived) and
        //    gating it behind arrival lost it. It is bounded at one FindPath per 1.5 units of target
        //    movement, so it is not the per-tick FindPath that gating on arrival was meant to avoid.
        //
        // MoveState/Speed are refreshed whenever the creature should be moving at all, which is
        // either of the above OR simply still walking with a destination that is still good.
        //
        // Do NOT call Stop merely because the creature is in attack range — Stop discards
        // the destination and is reserved for actually disengaging (leash, target switch, target
        // lost), not for "close enough to hit right now."
        bool stillWalking = !hasArrived;
        float driftThreshold = hasSlot ? Context.Locomotion.ArrivalTolerance(Creature) : PathRecalculationThreshold;
        // A settled creature out of range re-plans too, however little its destination moved (#606):
        // otherwise a target that stepped less than the drift threshold away (onto an island, say)
        // would leave it standing out of reach on a stale route that ended where it asked. At most one
        // FindPath a tick, only while it is stuck, and the unreachable limit ends that.
        bool settledOffDestination = !stillWalking &&
            (Vector3.Distance(currentPosition, destination) > driftThreshold || !inRange);
        bool destinationDrifted = stillWalking &&
            Vector3.Distance(_lastRequestedDestination, destination) > PathRecalculationThreshold;

        if (stillWalking || settledOffDestination)
        {
            if (settledOffDestination || destinationDrifted)
            {
                RequestMoveTo(destination);
            }

            Creature.MoveState = MoveState.Running;
            Creature.Speed = Creature.Metadata.SpeedRun;
        }
    }

    /// <summary>
    /// The only route this script has to the locomotion's <c>MoveTo</c>. Exists so
    /// <see cref="_lastRequestedDestination" /> cannot fall out of step with the journey actually in
    /// progress: the mid-walk staleness check in <see cref="KeepStation" /> is only as trustworthy as that
    /// record, and a record kept by hand at five call sites is one forgotten assignment away from a
    /// creature that either never re-paths or re-paths every tick.
    /// </summary>
    private void RequestMoveTo(Vector3 destination)
    {
        Context.Locomotion.MoveTo(Creature, destination);
        _lastRequestedDestination = destination;
    }

    /// <summary>
    /// Authoritative target picker. Honours an active taunt first (creature is forced onto
    /// the taunter until <see cref="ICreature.TauntExpiresAt"/> elapses), then falls back to
    /// the encounter's top-threat unit. Returns <c>null</c> when no encounter exists for
    /// this creature (e.g. very first tick after OnEnteredRange, before damage has been
    /// routed through <see cref="ICombatService"/>); callers keep the existing target in
    /// that case.
    /// </summary>
    public IUnit? PickTarget()
    {
        // Taunt override: while the taunt is active, the creature is locked onto the
        // taunter regardless of threat ordering. This mirrors CombatService.ApplyTaunt
        // which sets these fields and bumps threat above the current top.
        if (Creature.TauntedBy is { } tauntedBy && _time.GetUtcNow().UtcDateTime < Creature.TauntExpiresAt)
        {
            return tauntedBy;
        }

        IEncounter? encounter = Context.CombatService.GetEncounterFor(Creature);
        return encounter?.GetTopThreat(Creature);
    }

    /// <summary>
    /// Which ability to start now against <paramref name="target" />, or null for none this tick (#163).
    /// Called every tick the creature fights and is not already casting, in range or not. The base answers
    /// the basic when it is ready and <paramref name="target" /> is in its reach; a creature type's script
    /// overrides it with its rotation (see <see cref="Ready" />). Whatever it answers is started only if it is
    /// one of this creature's own abilities, ready, and in reach.
    /// </summary>
    /// <param name="distance">
    /// Metres on X/Z from the creature's centre to the edge of the target's body: what a shape must reach to
    /// hit it, since a shape hits every body it overlaps. Where the creature stands is pulled in so its basic
    /// reaches from anywhere its locomotion counts as arrived (see ChooseDestination).
    /// </param>
    protected virtual IAbility? ChooseAbility(IUnit target, float distance) =>
        _abilities.Basic is { } basic && CreatureAbilities.IsReady(basic) && InReach(basic, distance) ? basic : null;

    /// <summary>
    /// The creature's ability <paramref name="id" /> when it holds it, it is ready, and a target
    /// <paramref name="distance" /> away is in its reach; otherwise null. A rotation chains these,
    /// most preferred first.
    /// </summary>
    protected IAbility? Ready(AbilityId id, float distance) =>
        _abilities[id] is { } ability && CreatureAbilities.IsReady(ability) && InReach(ability, distance)
            ? ability
            : null;

    /// <summary>
    /// Whether a target whose body edge is <paramref name="distance" /> away is in <paramref name="ability" />'s
    /// reach: a cone's length, a projectile's travel, a circle on the caster's radius, and a circle on the aim
    /// point's reach plus its radius. A creature aims at its target, so the arc never leaves it out.
    /// </summary>
    protected static bool InReach(IAbility ability, float distance)
    {
        AbilityMetadata meta = ability.Metadata;
        float reach = meta.Shape switch
        {
            AbilityShape.Circle => meta.Anchor == AbilityAnchor.AimPoint ? meta.Reach + meta.Radius : meta.Radius,
            _ => meta.Reach,
        };

        return distance <= reach;
    }

    /// <summary>
    /// Starts the ability <see cref="ChooseAbility" /> picks, when it is one of the creature's own, ready and
    /// in reach, aimed at <paramref name="target" /> now. Returns whether a wind-up began, which stops the
    /// creature for the cast. A cast the cast system refuses (a script missing or that cannot be built) is
    /// simply not started.
    /// </summary>
    /// <param name="facing">Whether the creature already turned to the target this tick.</param>
    private bool TryAttack(IUnit target, Vector3 currentPosition, Vector3 targetPosition, bool facing)
    {
        float distance = MathF.Max(0f, HitShapes.Distance2D(currentPosition, targetPosition) - target.BodyRadius);
        if (ChooseAbility(target, distance) is not { } ability
            || !Holds(ability)
            || !CreatureAbilities.IsReady(ability)
            || !InReach(ability, distance))
        {
            return false;
        }

        if (!facing)
        {
            Creature.LookAt(targetPosition);
        }

        AbilityAim aim = AimAt(ability.Metadata, currentPosition, targetPosition);

        if (ability.Metadata.CastTime <= 0f)
        {
            Context.RunInstantAbility(Creature, aim, ability);
            return false;
        }

        if (!Context.QueueAbility(Creature, aim, ability))
        {
            return false;
        }

        // The start, with its footprint, went out with the queue (#648).
        Context.Locomotion.Stop(Creature);
        return true;
    }

    /// <summary>Whether <paramref name="ability" /> is one of this creature's own clones: a script casts only what it holds.</summary>
    private bool Holds(IAbility ability)
    {
        foreach (IAbility held in _abilities.All)
        {
            if (ReferenceEquals(held, ability))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The aim at the target, captured when the cast starts (#163): a Movement ability along the facing toward
    /// it, a Cursor one at its position, which the shape clamps by its own rules. A target standing on the
    /// creature falls back to the creature's facing.
    /// </summary>
    private AbilityAim AimAt(AbilityMetadata meta, Vector3 from, Vector3 to)
    {
        double dx = (double)to.x - from.x;
        double dz = (double)to.z - from.z;
        double length = Math.Sqrt(dx * dx + dz * dz);
        Vector3 facing = length > 0.001 && double.IsFinite(length)
            ? new Vector3((float)(dx / length), 0f, (float)(dz / length))
            : AbilityAim.FacingFromYaw(Creature.Orientation.y);

        return meta.AimMode == AbilityAimMode.Cursor ? new AbilityAim(facing, to) : new AbilityAim(facing, null);
    }

    private void ResetToIdleAtSpawn()
    {
        State = CombatState.None;

        // Defensive: every path that sets State = Returning already released and nulled _target
        // itself, so this is normally a no-op by the time it runs here — kept in case that ever
        // changes.
        if (_target is not null)
            Context.MeleeSlots.Release(_target.Guid, Creature.Guid);

        _target = null;
        _home = null;
        _unreachableFor = TimeSpan.Zero;
        _returningFor = TimeSpan.Zero;
        _returnStalledFor = TimeSpan.Zero;
        _closestToHome = float.MaxValue;

        // #627, #163: no cooldown carries into the next fight, whose first attack lands at once.
        _abilities.ResetCooldowns();
        Context.Locomotion.Stop(Creature);

        // The one place a fight ends at home (#614): the creature leaves its encounter and forgets any
        // taunt, so no threat from before the leash, seeded or earned, and no old taunter steers its
        // next fight. The players stay in the encounter.
        Creature.TauntedBy = null;
        Creature.TauntExpiresAt = DateTime.MinValue;
        (Context.CombatService as Avalon.World.Combat.IHostileEncounterExit)?.DropHostileFromEncounter(Creature);
    }
}
