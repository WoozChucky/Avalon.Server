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
    private Queue<Vector3> _currentPath = new();
    private uint _currentWaypointIndex;

    public override object State { get; set; } = PatrolState.Patrolling;

    protected override bool ShouldRun() => State is PatrolState.Patrolling;

    public override void Update(TimeSpan deltaTime)
    {
        if (waypoints.Length == 0)
        {
            return;
        }

        if (_currentPath.Count == 0)
        {
            Vector3 currentPosition = Creature.Position;
            Vector3 targetPosition = waypoints[_currentWaypointIndex];
            _currentPath = GeneratePath(currentPosition, targetPosition);
        }

        FollowPath(deltaTime);
    }

    public override void OnHit(IUnit attacker, uint damage) => State = PatrolState.Idle;

    private Queue<Vector3> GeneratePath(Vector3 currentPosition, Vector3 targetPosition)
    {
        List<Vector3> path = Context.GetNavigatorForPosition(currentPosition).FindPath(currentPosition, targetPosition);
        return new Queue<Vector3>(path);
    }

    private void FollowPath(TimeSpan deltaTime)
    {
        if (_currentPath.Count == 0)
        {
            _currentWaypointIndex = (_currentWaypointIndex + 1) % (uint)waypoints.Length;
            State = PatrolState.Idle;
            return;
        }

        Vector3 currentPosition = Creature.Position;
        Vector3 targetPosition = _currentPath.Peek();

        if (Vector3.Distance(currentPosition, targetPosition) < 0.1f)
        {
            _currentPath.Dequeue();
            if (_currentPath.Count == 0)
            {
                _currentWaypointIndex = (_currentWaypointIndex + 1) % (uint)waypoints.Length;
                State = PatrolState.Idle;
                Creature.MoveState = MoveState.Idle;
                return;
            }

            targetPosition = _currentPath.Peek();
        }

        Creature.Speed = Creature.Metadata.SpeedWalk;

        Vector3 direction = Vector3.Normalize(targetPosition - currentPosition);
        Vector3 movementDelta = direction * Creature.Speed * (float)deltaTime.TotalSeconds;

        Creature.MoveState = MoveState.Walking;
        Creature.Velocity = direction;
        Creature.Position += movementDelta;
        Creature.LookAt(targetPosition);
    }
}
