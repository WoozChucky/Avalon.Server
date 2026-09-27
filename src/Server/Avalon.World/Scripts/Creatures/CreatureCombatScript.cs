using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;
using Avalon.World.Combat;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Creatures;

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
    private const float AttackCooldown = 2.25f; // Cooldown between attacks

    // How long a creature may go without any way to reach its target before it gives up and goes
    // home, as it does past the leash (#606). "No way to reach" is decided from where the creature's
    // route really ends (ICreatureLocomotion.ResolvedDestination), never from HasArrived: a partial
    // route toward a target on a ledge or an island is walked like any other, so arrival flips on and
    // off while the creature stands at its end. See RouteCanReach. Only an unbroken stretch counts;
    // a tick in range or with a route that can reach starts the count over, as does a new target.
    private static readonly TimeSpan UnreachableGiveUpTime = TimeSpan.FromSeconds(5);

    private readonly ILogger<CreatureCombatScript> _logger;
    private readonly TimeProvider _time;
    private readonly ICombatRandom _random;
    private float _attackCooldownTimer;

    private bool _dead;

    // Where the fight began, and where the creature goes back to. Null while no fight has set it: any
    // position is a valid home, the origin included (#606).
    private Vector3? _home;

    // How long, without a break, the creature has had no way to reach its target (#606).
    private TimeSpan _unreachableFor;

    // The destination the journey now in progress was planned for. This is what the mid-walk
    // staleness check in KeepStation measures against, and it answers a different question from the
    // settled check next to it: "is where I am headed still where I should be headed" versus "have
    // I stopped somewhere that is no longer good enough". Written only by RequestMoveTo, which is
    // the single route this script has to the locomotion's MoveTo — so there is no way to start a
    // journey without recording what it was planned for.
    private Vector3 _lastRequestedDestination;

    private IUnit? _target;

    /// <param name="time">The container's clock, the one the rest of the world times by (#610).</param>
    /// <param name="random">Where the swing's damage roll comes from (#506); the container's, resolved by name.</param>
    public CreatureCombatScript(ILoggerFactory loggerFactory, ICreature creature, ISimulationContext context,
        TimeProvider? time = null, ICombatRandom? random = null) : base(creature, context)
    {
        _logger = loggerFactory.CreateLogger<CreatureCombatScript>();
        _time = time ?? TimeProvider.System;
        _random = random ?? new CombatRandom(Random.Shared);
    }

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
            State = CombatState.Returning;
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
                Creature.CurrentHealth = 0;
                _dead = true;

                // _dead short-circuits Update from here on, so this is the script's only chance
                // to give back whatever slot it held.
                // The kill itself (loot, experience, the corpse) is the combat service's to report to
                // the instance, once this hit returns (#546).
                if (_target is not null)
                    Context.MeleeSlots.Release(_target.Guid, Creature.Guid);

                return;
            }

            Creature.CurrentHealth -= damage;

            // A hit from a different unit switches target immediately, same as the top-threat
            // reconciliation in Update — release whatever slot was held on the old one first.
            if (!ReferenceEquals(_target, attacker))
            {
                if (_target is not null)
                    Context.MeleeSlots.Release(_target.Guid, Creature.Guid);

                _unreachableFor = TimeSpan.Zero;
            }

            _target = attacker;
            _home ??= Creature.Position;

            State = CombatState.Combat;
            Context.BroadcastUnitHit(attacker, Creature, Creature.CurrentHealth, damage);
        }
    }

    public override void Update(TimeSpan deltaTime)
    {
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
            UpdateReturning(currentPosition);
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

        Engage(_target, currentPosition, targetPosition, deltaTime);

        if (_unreachableFor > UnreachableGiveUpTime)
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
    /// when the last one ended short, and snapping home when there is none.
    /// </summary>
    private void UpdateReturning(Vector3 currentPosition)
    {
        if (Vector3.Distance(currentPosition, Home) < 0.1f)
        {
            ResetToIdleAtSpawn();
            return;
        }

        // The journey may be over either because MoveTo at transition-time failed (DotRecast
        // returned no route — happens when start/end land on disconnected nav polygons) or
        // because locomotion consumed the last waypoint without us hitting the < 0.1f gate
        // above (e.g. smoothed last point ≠ exact spawn). Without a regen the creature
        // drifts: server keeps Position static but MoveState=Running + Velocity is stale,
        // so the client extrapolates indefinitely.
        if (Context.Locomotion.HasArrived(Creature))
        {
            RequestMoveTo(Home);
            if (Context.Locomotion.HasArrived(Creature))
            {
                // Planner can't reach spawn — snap home rather than drift forever.
                Context.Locomotion.Teleport(Creature, Home);
                ResetToIdleAtSpawn();
                return;
            }
        }

        Creature.MoveState = MoveState.Running;
        Creature.Speed = Creature.Metadata.SpeedRun;
    }

    /// <summary>
    /// Drops <paramref name="target" /> and heads home at full health: the target died, the creature
    /// was drawn past <see cref="MaxChaseDistance" /> from where the fight began, or it had no way to
    /// reach the target for longer than <see cref="UnreachableGiveUpTime" /> (#606).
    /// </summary>
    private void GiveUpAndGoHome(IUnit target)
    {
        Context.MeleeSlots.Release(target.Guid, Creature.Guid);
        _target = null;
        _unreachableFor = TimeSpan.Zero;
        State = CombatState.Returning;
        RequestMoveTo(Home);
        Creature.CurrentHealth = Creature.Health;
    }

    /// <summary>
    /// One tick of fighting <paramref name="target" />: pick where to stand, swing when in range, and
    /// keep walking to where it should stand, in that order.
    /// </summary>
    private void Engage(IUnit target, Vector3 currentPosition, Vector3 targetPosition, TimeSpan deltaTime)
    {
        bool hasSlot = ChooseDestination(target, currentPosition, targetPosition, out Vector3 destination);

        bool hasArrived = Context.Locomotion.HasArrived(Creature);

        // Attacking and keeping station are independent: a creature can swing at the target the
        // instant it is within range, in the very same tick it is also stepping to keep pace with
        // a target (and so a slot) that is on the move — Stop is never called merely for being in
        // range (see KeepStation). But the allowance beyond plain AttackRange is gated on arrival
        // alone (see AttackRangeArrivalMargin's comment for the full reasoning, including why
        // hasSlot is deliberately NOT part of this gate).
        float effectiveAttackRange = hasArrived
            ? AttackRange + Context.Locomotion.ArrivalTolerance(Creature) + AttackRangeArrivalMargin
            : AttackRange;

        bool inRange = Vector3.Distance(currentPosition, targetPosition) <= effectiveAttackRange;
        if (inRange)
        {
            Creature.LookAt(targetPosition);
            AttackTarget(deltaTime);
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
        return hasSlot;
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

    private void AttackTarget(TimeSpan deltaTime)
    {
        // Logic to attack the target
        if (_attackCooldownTimer <= 0.0f)
        {
            Context.BroadcastAttackAnimation(Creature, null); // TODO: spells for creatures
            // Route melee through CombatService so threat / encounter membership / death broadcast
            // all trigger from the canonical chokepoint (spec section 3: "all damage funnels ApplyDamage").
            // No ability — uses the raw-damage overload with default ThreatMultiplier=1.0.
            if (_target is not null)
            {
                Context.CombatService.ApplyDamage(Creature, _target, RollDamage());
            }
            _attackCooldownTimer = AttackCooldown;
        }
        else
        {
            _attackCooldownTimer -= (float)deltaTime.TotalSeconds;
        }
    }

    /// <summary>
    /// Damage comes from the creature's derived range rather than a constant. Inclusive of both bounds,
    /// and safe when the range is a single value — a degenerate range must deal exactly that, not zero
    /// and not one more.
    /// </summary>
    private uint RollDamage()
    {
        uint min = Creature.DamageMin;
        uint max = Math.Max(min, Creature.DamageMax);

        return min == max ? min : (uint)_random.NextInt64(min, max);
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
        Context.Locomotion.Stop(Creature);

        // The one place a fight ends at home (#614): the creature leaves its encounter and forgets any
        // taunt, so no threat from before the leash, seeded or earned, and no old taunter steers its
        // next fight. The players stay in the encounter.
        Creature.TauntedBy = null;
        Creature.TauntExpiresAt = DateTime.MinValue;
        (Context.CombatService as Avalon.World.Combat.IHostileEncounterExit)?.DropHostileFromEncounter(Creature);
    }
}
