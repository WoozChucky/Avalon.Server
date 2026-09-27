using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Creatures;

/// <summary>
/// Walks the creature's <see cref="ICreature.PatrolPath"/> in order, looping, standing at each
/// point for that point's <see cref="PatrolPoint.Wait"/> before walking on. A creature with no path
/// stands where it was placed.
/// </summary>
/// <remarks>
/// <para>
/// The path is read from the creature, not taken as a constructor argument: placement builds every
/// AI script with exactly <c>(creature, instance)</c> as runtime arguments (see
/// <c>CreaturePlacementService.AttachScript</c>), so a script needing its route in the constructor
/// cannot be attached from a <c>ScriptName</c> at all (#421).
/// </para>
/// <para>
/// A patrolling creature fights when hit, then resumes its path (#600). It chains a
/// <see cref="CreatureCombatScript"/>, as <see cref="AggroDefendScript"/> does, and hits reach it
/// through the chain. While the combat script is in any state but
/// <see cref="CreatureCombatScript.CombatState.None"/> it drives the creature, and the patrol neither
/// moves nor advances; once it is back to <c>None</c> (home again after the leash or a lost target),
/// the patrol picks up at the path point nearest the creature and walks on in the path's order. A
/// corpse's patrol stops for good.
/// </para>
/// </remarks>
public sealed class CreaturePatrolScript : AiScript
{
    public enum PatrolState
    {
        Patrolling,

        /// <summary>Handed over to the chained combat script until the fight is over.</summary>
        Idle
    }

    private readonly AiScript _combat;
    private int _currentWaypointIndex;
    private bool _hasRequestedCurrentWaypoint;
    private bool _pausing;
    private TimeSpan _pauseRemaining;

    public CreaturePatrolScript(ILoggerFactory loggerFactory, ICreature creature, ISimulationContext context)
        : base(creature, context)
    {
        _combat = new CreatureCombatScript(loggerFactory, creature, context);
        Chain(_combat);
    }

    public override object State { get; set; } = PatrolState.Patrolling;

    protected override bool ShouldRun() => State is PatrolState.Patrolling;

    private bool InCombat => _combat.State is not CreatureCombatScript.CombatState.None;

    public override void Update(TimeSpan deltaTime)
    {
        // A corpse neither patrols nor fights (#600). Read from the creature, not remembered from
        // the killing hit: a hot reload builds a fresh script for every creature, corpses included.
        if (Creature.CurrentHealth == 0)
        {
            return;
        }

        if (InCombat)
        {
            if (State is PatrolState.Patrolling)
            {
                // Hand over: end the leg in progress, so the combat script starts from rest rather
                // than inheriting a walk to a waypoint it never asked for.
                State = PatrolState.Idle;
                Context.Locomotion.Stop(Creature);
            }

            // Only the combat script runs while it fights; the patrol's point and wait stay put.
            base.Update(deltaTime);
            if (InCombat)
            {
                return;
            }
        }

        if (State is PatrolState.Idle)
        {
            ResumeFromNearestPoint();
        }

        Patrol(deltaTime);
    }

    private void Patrol(TimeSpan deltaTime)
    {
        IReadOnlyList<PatrolPoint> waypoints = Creature.PatrolPath;
        if (waypoints.Count == 0)
        {
            return;
        }

        // The path is the creature's, so it can be replaced between ticks; never index past it.
        if (_currentWaypointIndex >= waypoints.Count)
        {
            _currentWaypointIndex = 0;
            _hasRequestedCurrentWaypoint = false;
            _pausing = false;
        }

        if (_pausing)
        {
            // Standing at the point. No MoveState write: locomotion set Idle when the creature came
            // to rest, and writing Walking here would stomp it every tick of the pause.
            _pauseRemaining -= deltaTime;
            if (_pauseRemaining > TimeSpan.Zero)
            {
                return;
            }

            _pausing = false;
            AdvanceToNextWaypoint(waypoints.Count);
            return;
        }

        if (Context.Locomotion.HasArrived(Creature))
        {
            if (_hasRequestedCurrentWaypoint)
            {
                // Locomotion has nothing left to walk towards, and we already asked it to head
                // here — either it genuinely finished the leg, or the waypoint was unreachable
                // and it came to rest immediately. Either way, sitting here forever is worse than
                // moving on, so pause if the point asks for it, then advance. What we must not do
                // is treat THIS same "no destination" signal as "arrived" before we have ever
                // asked locomotion to go anywhere, which is why the very first request below
                // doesn't fall into this branch.
                TimeSpan wait = waypoints[_currentWaypointIndex].Wait;
                if (wait > TimeSpan.Zero)
                {
                    _pausing = true;
                    _pauseRemaining = wait;
                    return;
                }

                AdvanceToNextWaypoint(waypoints.Count);
                return;
            }

            Context.Locomotion.MoveTo(Creature, waypoints[_currentWaypointIndex].Position);
            _hasRequestedCurrentWaypoint = true;
        }

        Creature.MoveState = MoveState.Walking;
        Creature.Speed = Creature.Metadata.SpeedWalk;
    }

    /// <summary>
    /// The fight is over: walk next to the path point nearest where the creature now stands, not to
    /// point 0 and not to the point it was heading for when it was hit, then on in the path's order.
    /// A pause the fight interrupted is dropped.
    /// </summary>
    private void ResumeFromNearestPoint()
    {
        State = PatrolState.Patrolling;
        _hasRequestedCurrentWaypoint = false;
        _pausing = false;

        IReadOnlyList<PatrolPoint> waypoints = Creature.PatrolPath;
        Vector3 position = Creature.Position;
        float nearest = float.MaxValue;
        for (int i = 0; i < waypoints.Count; i++)
        {
            float distance = Vector3.Distance(position, waypoints[i].Position);
            if (distance < nearest)
            {
                nearest = distance;
                _currentWaypointIndex = i;
            }
        }
    }

    // Deliberately leaves State alone. It used to set Idle, which — with ShouldRun() being
    // "State is Patrolling" — would have stopped a chained patrol for good after its first leg.
    private void AdvanceToNextWaypoint(int count)
    {
        _currentWaypointIndex = (_currentWaypointIndex + 1) % count;
        _hasRequestedCurrentWaypoint = false;
    }
}
