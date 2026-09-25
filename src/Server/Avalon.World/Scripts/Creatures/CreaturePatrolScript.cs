using Avalon.Network.Packets.State;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Creatures;

/// <summary>
/// Walks the creature's <see cref="ICreature.PatrolPath"/> in order, looping, standing at each
/// point for that point's <see cref="PatrolPoint.Wait"/> before walking on. A creature with no path
/// stands where it was placed.
/// </summary>
/// <remarks>
/// The path is read from the creature, not taken as a constructor argument: placement builds every
/// AI script with exactly <c>(creature, instance)</c> as runtime arguments (see
/// <c>CreaturePlacementService.AttachScript</c>), so a script needing its route in the constructor
/// cannot be attached from a <c>ScriptName</c> at all (#421).
/// </remarks>
public sealed class CreaturePatrolScript(
    ILoggerFactory loggerFactory,
    ICreature creature,
    ISimulationContext context)
    : AiScript(creature, context)
{
    public enum PatrolState
    {
        Patrolling,
        Idle
    }

    private readonly ILogger<CreaturePatrolScript> _logger = loggerFactory.CreateLogger<CreaturePatrolScript>();
    private int _currentWaypointIndex;
    private bool _hasRequestedCurrentWaypoint;
    private bool _pausing;
    private TimeSpan _pauseRemaining;

    public override object State { get; set; } = PatrolState.Patrolling;

    protected override bool ShouldRun() => State is PatrolState.Patrolling;

    public override void Update(TimeSpan deltaTime)
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
    /// Stops the patrol, so a script chained alongside it (combat, say) can take over. Top-level
    /// scripts are ticked unconditionally by <c>MapInstance</c>, so this only matters when chained.
    /// </summary>
    public override void OnHit(IUnit attacker, uint damage) => State = PatrolState.Idle;

    // Deliberately leaves State alone. It used to set Idle, which — with ShouldRun() being
    // "State is Patrolling" — would have stopped a chained patrol for good after its first leg.
    private void AdvanceToNextWaypoint(int count)
    {
        _currentWaypointIndex = (_currentWaypointIndex + 1) % count;
        _hasRequestedCurrentWaypoint = false;
    }
}
