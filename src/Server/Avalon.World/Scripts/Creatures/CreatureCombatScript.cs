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

    // How far a settled surplus (no-slot) creature's target may wander before its destination
    // (a stand-off point at AttackRange from the target, on this creature's own bearing — see
    // Update) counts as drifted enough to re-engage movement. A slotted creature uses
    // Context.Locomotion.ArrivalTolerance instead: see Update.
    private const float PathRecalculationThreshold = 1.5f;

    // Gated on locomotion reporting arrival (see hasArrived in Update) — not unconditional, and
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
    // the movement half of Update, which only re-engages once the gap exceeds this SAME
    // tolerance, not to shrink it further — so without the margin a creature that happened to
    // settle a hair past AttackRange would be stuck there, forever, attacking nothing. The margin
    // below is that same locomotion-sourced tolerance (see the note on
    // Context.Locomotion.ArrivalTolerance for why it has to come from there rather than a
    // constant here) plus a small fixed buffer against float noise in the distance comparison
    // itself.
    private const float AttackRangeArrivalMargin = 0.05f;
    private const float AttackCooldown = 2.25f; // Cooldown between attacks
    private readonly ILogger<CreatureCombatScript> _logger;
    private float _attackCooldownTimer;

    private bool _dead;
    private Vector3 _initialPosition;

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
        // Computed once here so movement below only pays for one TryClaim per tick (idempotent —
        // see MeleeSlots.TryClaim — but there is no reason to call it twice).
        bool hasSlot = Context.MeleeSlots.TryClaim(_target.Guid, Creature.Guid, targetPosition, currentPosition, out int slot);
        Vector3 destination = hasSlot
            ? Context.MeleeSlots.PositionFor(targetPosition, slot)
            : targetPosition + Vector3.Normalize(currentPosition - targetPosition) * (AttackRange - SurplusStandOffInset);

        bool hasArrived = Context.Locomotion.HasArrived(Creature);

        // Attacking and keeping station are independent: a creature can swing at the target the
        // instant it is within range, in the very same tick it is also stepping to keep pace with
        // a target (and so a slot) that is on the move — Stop is never called merely for being in
        // range (see below). But the allowance beyond plain AttackRange is gated on arrival alone
        // (see AttackRangeArrivalMargin's comment for the full reasoning, including why hasSlot is
        // deliberately NOT part of this gate).
        float effectiveAttackRange = hasArrived
            ? AttackRange + Context.Locomotion.ArrivalTolerance(Creature) + AttackRangeArrivalMargin
            : AttackRange;

        if (Vector3.Distance(currentPosition, targetPosition) <= effectiveAttackRange)
        {
            Creature.LookAt(targetPosition);
            AttackTarget(deltaTime);
        }

        // Keep adjusting footing while still walking (locomotion hasn't reported arrival), or —
        // once settled — while the destination has since drifted away from where the creature is
        // standing by more than its arrival tolerance. The second half is what makes a moving
        // target's slot get chased rather than abandoned: locomotion has no idea the target moved
        // once it has gone idle, so without it a creature that settled at its slot would simply
        // stay there forever as the target (and so the slot) walks away. A slotted creature uses
        // the locomotion's own arrival tolerance for "how far is too far to ignore" — the same
        // idea as PathRecalculationThreshold, applied to the slot instead of the raw target
        // position, and reusing ArrivalTolerance rather than a second invented constant since it
        // already answers exactly "how close counts as close enough" for this locomotion. A
        // surplus (no-slot) creature keeps PathRecalculationThreshold, unchanged, since nothing
        // about how sensitively it should re-path changed this round.
        //
        // Do NOT call Stop merely because the creature is in attack range above — Stop discards
        // the destination and is reserved for actually disengaging (leash, target switch, target
        // lost), not for "close enough to hit right now."
        bool stillWalking = !hasArrived;
        float driftThreshold = hasSlot ? Context.Locomotion.ArrivalTolerance(Creature) : PathRecalculationThreshold;

        if (stillWalking || Vector3.Distance(currentPosition, destination) > driftThreshold)
        {
            if (!stillWalking)
            {
                Context.Locomotion.MoveTo(Creature, destination);
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
