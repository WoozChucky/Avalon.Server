using Avalon.Common.Mathematics;
using Avalon.World.Public;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Creatures;

public sealed class CreaturePatrolScript : AiScript
{
    public enum PatrolState
    {
        Patrolling,
        Idle
    }

    private readonly ILogger<CreaturePatrolScript> _logger;
    private readonly Vector3[] _waypoints;
    private uint _currentWaypointIndex = 0;
    
    public CreaturePatrolScript(ILoggerFactory loggerFactory, ICreature creature, IChunk chunk, Vector3[] waypoints) : base(creature, chunk)
    {
        _logger = loggerFactory.CreateLogger<CreaturePatrolScript>();
        _waypoints = waypoints;
    }

    public override object State { get; set; }
    
    protected override bool ShouldRun()
    {
        return State is PatrolState.Patrolling;
    }

    public override void Update(TimeSpan deltaTime)
    {
        var currentPosition = Creature.Position;
        var targetPosition = _waypoints[_currentWaypointIndex];
        
        if (Vector3.Distance(currentPosition, targetPosition) < 0.1f)
        {
            _currentWaypointIndex = (_currentWaypointIndex + 1) % (uint)_waypoints.Length;
            State = PatrolState.Idle;
        }
        else
        {
            var direction = Vector3.Normalize(targetPosition - currentPosition);
            
            var movementDelta = direction * Creature.Speed * (float)deltaTime.TotalSeconds;
        
            Creature.Velocity = direction;
            Creature.Position += movementDelta;
        }
    }
}
