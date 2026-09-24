using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Creatures;

public sealed class CreaturePatrolScript(
    ILoggerFactory loggerFactory,
    ICreature creature,
    ISimulationContext context,
    Vector3[] waypoints)
    : AiScript(creature, context)
{
    public enum PatrolState
    {
        Patrolling,
        Idle
    }

    private readonly ILogger<CreaturePatrolScript> _logger = loggerFactory.CreateLogger<CreaturePatrolScript>();
    private uint _currentWaypointIndex;
    private bool _hasRequestedCurrentWaypoint;

    public override object State { get; set; } = PatrolState.Patrolling;

    protected override bool ShouldRun() => State is PatrolState.Patrolling;

    public override void Update(TimeSpan deltaTime)
    {
        if (waypoints.Length == 0)
        {
            return;
        }

        if (Context.Locomotion.HasArrived(Creature))
        {
            if (_hasRequestedCurrentWaypoint)
            {
                // Locomotion has nothing left to walk towards, and we already asked it to head
                // here — either it genuinely finished the leg, or the waypoint was unreachable
                // and it came to rest immediately. Either way, sitting here forever is worse than
                // moving on, so advance. What we must not do is treat THIS same "no destination"
                // signal as "arrived" before we have ever asked locomotion to go anywhere, which
                // is why the very first request below doesn't fall into this branch.
                AdvanceToNextWaypoint();
                return;
            }

            Context.Locomotion.MoveTo(Creature, waypoints[_currentWaypointIndex]);
            _hasRequestedCurrentWaypoint = true;
        }

        Creature.MoveState = MoveState.Walking;
        Creature.Speed = Creature.Metadata.SpeedWalk;
    }

    public override void OnHit(IUnit attacker, uint damage) => State = PatrolState.Idle;

    private void AdvanceToNextWaypoint()
    {
        _currentWaypointIndex = (_currentWaypointIndex + 1) % (uint)waypoints.Length;
        _hasRequestedCurrentWaypoint = false;
        State = PatrolState.Idle;
    }
}
