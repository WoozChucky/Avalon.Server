using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.State;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;
using Avalon.World.Public.Scripts;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Creatures;

public sealed class CreaturePatrolScript : AiScript
{
    public enum PatrolState
    {
        Patrolling,
        Idle,
    }

    private readonly ILogger<CreaturePatrolScript> _logger;
    private readonly Vector3[] _waypoints;
    private uint _currentWaypointIndex = 0;
    private Queue<Vector3> _currentPath;
    
    public CreaturePatrolScript(ILoggerFactory loggerFactory, ICreature creature, IChunk chunk, Vector3[] waypoints) : base(creature, chunk)
    {
        _logger = loggerFactory.CreateLogger<CreaturePatrolScript>();
        _waypoints = waypoints;
        _currentPath = new Queue<Vector3>();
    }

    public override object State { get; set; } = PatrolState.Patrolling;
    
    protected override bool ShouldRun()
    {
        return State is PatrolState.Patrolling;
    }

    public override void Update(TimeSpan deltaTime)
    {
        if (_waypoints.Length == 0)
        {
            return;
        }
        
        if (_currentPath.Count == 0)
        {
            var currentPosition = Creature.Position;
            var targetPosition = _waypoints[_currentWaypointIndex];
            _currentPath = GeneratePath(currentPosition, targetPosition);
        }
        
        FollowPath(deltaTime);
    }

    public override void OnHit(IUnit attacker, uint damage)
    {
        State = PatrolState.Idle;
    }

    private Queue<Vector3> GeneratePath(Vector3 currentPosition, Vector3 targetPosition)
    {
        var path = Chunk.Navigator.FindPath(currentPosition, targetPosition);
        return new Queue<Vector3>(path);
    }

    private void FollowPath(TimeSpan deltaTime)
    {
        if (_currentPath.Count == 0)
        {
            _currentWaypointIndex = (_currentWaypointIndex + 1) % (uint) _waypoints.Length;
            State = PatrolState.Idle;
            return;
        }
        
        var currentPosition = Creature.Position;
        var targetPosition = _currentPath.Peek();

        if (Vector3.Distance(currentPosition, targetPosition) < 0.1f)
        {
            _currentPath.Dequeue();
            if (_currentPath.Count == 0)
            {
                _currentWaypointIndex = (_currentWaypointIndex + 1) % (uint) _waypoints.Length;
                State = PatrolState.Idle;
                Creature.MoveState = MoveState.Idle;
                return;
            }
            targetPosition = _currentPath.Peek();
        }

        Creature.Speed = Creature.Metadata.SpeedWalk;
        
        var direction = Vector3.Normalize(targetPosition - currentPosition);
        var movementDelta = direction * Creature.Speed * (float) deltaTime.TotalSeconds;

        Creature.MoveState = MoveState.Walking;
        Creature.Velocity = direction;
        Creature.Position += movementDelta;
        Creature.LookAt(targetPosition);
    }
}
