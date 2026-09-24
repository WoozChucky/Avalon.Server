using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;
using Avalon.World.Entities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Creatures;

public class CreatureCombatScript : AiScript
{
    public enum CombatState
    {
        None,
        Combat,
        Chase,
        Returning
    }

    // This is the position distance at which the creature will stop chasing the target if no hits were received in the meantime, and if the creature itself didn't hit the target
    private const float MaxChaseDistance = 40.0f;
    private const float AttackRange = 1.5f;
    private const float PathRecalculationThreshold = 1.5f; // Threshold to recalculate the path

    // MeleeSlotRadius defaults to the same value as AttackRange, so a creature standing in its
    // claimed slot is already in range to attack. That puts it exactly on the AttackRange
    // boundary by construction, and WaypointLocomotion's own 0.1f arrival epsilon means it stops
    // NEAR its slot, not on it, so raw distance-to-target-centre lands either side of the
    // boundary depending on float noise. This tolerance only ever applies once a creature has
    // both claimed a slot and locomotion reports arrival there (see the in-range check in
    // Update): at that point distance-to-centre is provably within [AttackRange - 0.1f,
    // AttackRange + 0.1f] by the triangle inequality, so any tolerance > 0.1f closes the gap.
    // It never changes behaviour while still approaching, and it never touches AttackRange
    // itself, so it is not a balance change.
    private const float AttackRangeArrivalTolerance = 0.15f;
    private const float AttackCooldown = 2.25f; // Cooldown between attacks
    private readonly ILogger<CreatureCombatScript> _logger;
    private float _attackCooldownTimer;

    private bool _dead;
    private Vector3 _initialPosition;
    private Vector3 _lastKnownTargetPosition;

    private IUnit? _target;

    public CreatureCombatScript(ILoggerFactory loggerFactory, ICreature creature, ISimulationContext context) : base(creature, context)
    {
        _logger = loggerFactory.CreateLogger<CreatureCombatScript>();
        _initialPosition = Vector3.zero;
        CharacterEntity.CharacterDisconnected += OnCharacterDisconnected;
    }

    public override object State { get; set; } = CombatState.None;

    private void OnCharacterDisconnected(ICharacter character)
    {
        if (_target == character && !_dead)
        {
            // Release before nulling _target — Release needs the target's guid.
            Context.MeleeSlots.Release(_target.Guid, Creature.Guid);
            _target = null;
            State = CombatState.Returning;
            Creature.CurrentHealth = Creature.Health;
            Context.Locomotion.MoveTo(Creature, _initialPosition);
        }
    }

    protected override bool ShouldRun() => State is CombatState.Combat or CombatState.Chase or CombatState.Returning;

    public override void OnEnteredRange(ICharacter character)
    {
        if (State is CombatState.None)
        {
            _target = character;
            _initialPosition = Creature.Position;
            _lastKnownTargetPosition = character.Position;
            State = CombatState.Combat;
        }
    }

    public override void OnHit(IUnit attacker, uint damage)
    {
        if (State is not CombatState.Returning)
        {
            Creature.CurrentHealth -= damage;
            if (Creature.CurrentHealth <= 0)
            {
                _logger.LogInformation("{Name} has died", Creature.Name);
                Creature.CurrentHealth = 0;
                _dead = true;

                // _dead short-circuits Update from here on, so this is the script's only chance
                // to give back whatever slot it held.
                if (_target is not null)
                    Context.MeleeSlots.Release(_target.Guid, Creature.Guid);

                Creature.Died(attacker);
                return;
            }

            // A hit from a different unit switches target immediately, same as the top-threat
            // reconciliation in Update — release whatever slot was held on the old one first.
            if (_target is not null && !ReferenceEquals(_target, attacker))
                Context.MeleeSlots.Release(_target.Guid, Creature.Guid);

            _target = attacker;
            if (_initialPosition == Vector3.zero)
            {
                _initialPosition = Creature.Position;
            }

            _lastKnownTargetPosition = attacker.Position;
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
        if (State is CombatState.Combat or CombatState.Chase)
        {
            IUnit? picked = PickTarget();
            if (picked is not null && !ReferenceEquals(picked, _target))
            {
                if (_target is not null)
                    Context.MeleeSlots.Release(_target.Guid, Creature.Guid);

                _target = picked;
                _lastKnownTargetPosition = picked.Position;
                Context.Locomotion.Stop(Creature);
            }
        }

        if (State is CombatState.Returning)
        {
            if (Vector3.Distance(currentPosition, _initialPosition) < 0.1f)
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
                Context.Locomotion.MoveTo(Creature, _initialPosition);
                if (Context.Locomotion.HasArrived(Creature))
                {
                    // Planner can't reach spawn — snap home rather than drift forever.
                    Context.Locomotion.Teleport(Creature, _initialPosition);
                    ResetToIdleAtSpawn();
                    return;
                }
            }

            Creature.MoveState = MoveState.Running;
            Creature.Speed = Creature.Metadata.SpeedRun;
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
            Context.MeleeSlots.Release(_target.Guid, Creature.Guid);
            _target = null;
            State = CombatState.Returning;
            Context.Locomotion.MoveTo(Creature, _initialPosition);
            Creature.CurrentHealth = Creature.Health;
            return;
        }

        Vector3 targetPosition = _target.Position;

        if (Vector3.Distance(currentPosition, _initialPosition) > MaxChaseDistance)
        {
            Context.MeleeSlots.Release(_target.Guid, Creature.Guid);
            _target = null;
            State = CombatState.Returning;
            Context.Locomotion.MoveTo(Creature, _initialPosition);
            Creature.CurrentHealth = Creature.Health;
            return;
        }

        // Claimed once per tick here (idempotent — see MeleeSlots.TryClaim) so both the in-range
        // check below and ChaseDestination agree on whether this creature currently holds a slot.
        bool hasSlot = Context.MeleeSlots.TryClaim(_target.Guid, Creature.Guid, targetPosition, currentPosition, out _);

        // The in-range decision is slot-relative, not merely distance-relative. A world-fixed
        // slot rarely sits on a chaser's approach bearing, so crossing the AttackRange circle on
        // the way in is typically some OTHER chaser's slot position, not this creature's own —
        // checking raw distance alone (ungated on arrival) stops every approaching creature at
        // roughly the same ring-crossing point regardless of which slot it claimed, collapsing a
        // crowd onto a couple of spots. So while a creature holds a slot, it only counts as in
        // range once locomotion reports it has actually arrived there (see
        // AttackRangeArrivalTolerance for why raw distance alone is boundary-sensitive right at
        // that moment). A creature with no slot (surplus — the ring was full when it asked) has
        // no "its own spot" to arrive at, so it keeps the plain, ungated distance test and piles
        // onto the centre exactly as it always has.
        bool inAttackRange = hasSlot
            ? Context.Locomotion.HasArrived(Creature) &&
              Vector3.Distance(currentPosition, targetPosition) <= AttackRange + AttackRangeArrivalTolerance
            : Vector3.Distance(currentPosition, targetPosition) <= AttackRange;

        if (inAttackRange)
        {
            Context.Locomotion.Stop(Creature);
            Creature.LookAt(targetPosition);
            AttackTarget(deltaTime);
        }
        else
        {
            if (Context.Locomotion.HasArrived(Creature) ||
                Vector3.Distance(_lastKnownTargetPosition, targetPosition) > PathRecalculationThreshold)
            {
                Context.Locomotion.MoveTo(Creature, ChaseDestination(_target));
                _lastKnownTargetPosition = targetPosition;
            }

            Creature.MoveState = MoveState.Running;
            Creature.Speed = Creature.Metadata.SpeedRun;
        }
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
        if (Creature.TauntedBy is { } tauntedBy && DateTime.UtcNow < Creature.TauntExpiresAt)
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
            Creature.SendAttackAnimation(null); // TODO: spells for creatures
            // Route melee through CombatService so threat / encounter membership / death broadcast
            // all trigger from the canonical chokepoint (spec section 3: "all damage funnels ApplyDamage").
            // No ability — uses the raw-damage overload with default ThreatMultiplier=1.0.
            if (_target is not null)
            {
                Context.CombatService.ApplyDamage(Creature, _target, 10);
            }
            _attackCooldownTimer = AttackCooldown;
        }
        else
        {
            _attackCooldownTimer -= (float)deltaTime.TotalSeconds;
        }
    }

    /// <summary>A full ring is not a reason to stop chasing: pile onto the centre as before.</summary>
    private Vector3 ChaseDestination(IUnit target)
    {
        return Context.MeleeSlots.TryClaim(target.Guid, Creature.Guid, target.Position, Creature.Position, out int slot)
            ? Context.MeleeSlots.PositionFor(target.Position, slot)
            : target.Position;
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
        _initialPosition = Vector3.zero;
        Context.Locomotion.Stop(Creature);
    }
}
