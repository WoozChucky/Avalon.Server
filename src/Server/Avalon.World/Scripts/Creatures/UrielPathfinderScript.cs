using Avalon.Common.Mathematics;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Creatures;

internal enum UrielState
{
    Patrolling,
    Attacking
}

public class UrielPathfinderScript : AiScript
{
    private readonly ILogger<UrielPathfinderScript> _logger;
    
    private bool _isPathfinding = false;
    private bool _hasWaypoints = false;
    private List<Vector3>? _waypoints;
    private ushort _currentWaypointIndex = 0;
    private ICharacter _target;
    private Vector3 _initialTargetPosition;
    
    private const float WaypointTolerance = 0.1f;
    private const float TargetTolerance = 0.25f;
    
    public UrielPathfinderScript(ILoggerFactory loggerFactory, ICreature creature, IChunk chunk) : base(creature, chunk)
    {
        _logger = loggerFactory.CreateLogger<UrielPathfinderScript>();
        State = UrielState.Patrolling;

        Chain(this);
    }
    
    public override object State { get; set; }
    protected override bool ShouldRun()
    {
        throw new NotImplementedException();
    }

    public override void OnHit(ICharacter attacker, uint damage)
    {
        _logger.LogInformation("{Name} has been hit by {Attacker} for {Damage} damage. Current Health: {Current}", Creature.Name, attacker.Name, damage, Creature.CurrentHealth);
        Creature.CurrentHealth -= damage;
        if (Creature.CurrentHealth <= 0)
        {
            _logger.LogInformation("{Name} has died", Creature.Name);
            Creature.CurrentHealth = Creature.Health; // reset health while developing
        }
        base.OnHit(attacker, damage);
    }

    public override void Update(TimeSpan deltaTime)
    {
        if (!_isPathfinding)
        {
            if (Chunk.GetConnections().Count == 0) return;
            var connection = Chunk.GetConnections().FirstOrDefault()!;
            if (connection.Character == null) return;
            
            _isPathfinding = true;
            _target = connection.Character;
            _initialTargetPosition = _target.Position;
        }
        else
        {
            // Check if target has moved beyond TargetTolerance
            if (Vector3.Distance(_target.Position, _initialTargetPosition) > TargetTolerance)
            {
                // Reset pathfinding and regenerate path but continue smoothly
                _initialTargetPosition = _target.Position;

                var currentPosition = Creature.Position;

                // Generate smooth path from NavMesh
                var path = Chunk.Navigator.FindPath(currentPosition, _target.Position);

                if (path.Count == 0)
                {
                    _hasWaypoints = false;
                    return;
                }

                _waypoints = path;
                _currentWaypointIndex = 1;
                _hasWaypoints = true;
            }

            if (_hasWaypoints && _waypoints != null && _currentWaypointIndex < _waypoints.Count)
            {
                MoveAlongPath(deltaTime);
            }
            else
            {
                _isPathfinding = false;
                _hasWaypoints = false;
                _currentWaypointIndex = 0;
            }
        }
    }

    private void MoveAlongPath(TimeSpan deltaTime)
    {
        var currentPosition = Creature.Position;
        var targetPosition = _waypoints![_currentWaypointIndex];
        var direction = (targetPosition - currentPosition).normalized;
        var distance = Vector3.Distance(currentPosition, targetPosition);
        
        if (distance <= WaypointTolerance)
        {
            _currentWaypointIndex++;
            if (_currentWaypointIndex >= _waypoints.Count)
            {
                _logger.LogDebug("Reached final waypoint");
                Creature.Velocity = Vector3.zero;
                return;
            }
            targetPosition = _waypoints[_currentWaypointIndex];
            direction = (targetPosition - currentPosition).normalized;
            distance = Vector3.Distance(currentPosition, targetPosition);
        }
        
        var movementDelta = direction * Creature.Speed * (float)deltaTime.TotalSeconds;

        if (CheckCollision(movementDelta))
        {
            // Attempt to adjust movement by checking individual axes
            var moveDeltaX = movementDelta with {z = 0 };
            var moveDeltaZ = movementDelta with {x = 0};
            
            if (!CheckCollision(moveDeltaX))
            {
                Creature.Position += moveDeltaX;
            }
            else if (!CheckCollision(moveDeltaZ))
            {
                Creature.Position += moveDeltaZ;
            }
            else
            {
                _logger.LogDebug("Collision detected at: {Position}", currentPosition);
                Creature.Velocity = Vector3.zero;
            }
        }
        else
        {
            Creature.Velocity = direction;
            Creature.Position += movementDelta;
        }
        
        
        // Adjust direction and speed for next frame
        if (distance <= WaypointTolerance)
        {
            _currentWaypointIndex++;
        }
    }

    private bool CheckCollision(Vector2 movementDelta)
    {
        // TODO: Implement collision detection
        return false;
    }
}
