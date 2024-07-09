using Avalon.Common.Mathematics;
using Avalon.Domain.Characters;
using Avalon.World.Entities;
using Avalon.World.Pathfinding;

namespace Avalon.World.Scripts.Creatures;

public class UrielPathfinderScript : AiScript
{
    private bool _isPathfinding = false;
    private bool _hasWaypoints = false;
    private List<Vector3>? _waypoints;
    private ushort _currentWaypointIndex = 0;
    private Character _target;
    private Vector3 _initialTargetPosition;
    
    private const float WaypointTolerance = 0.1f;
    private const float TargetTolerance = 0.25f;
    
    public UrielPathfinderScript(Creature creature, Chunk chunk) : base(creature, chunk)
    {
        
    }
    
    
    public override async Task Update(TimeSpan deltaTime)
    {
        if (!_isPathfinding)
        {
            if (Chunk.GetConnections().Count == 0) return;
            var connection = Chunk.GetConnections().FirstOrDefault()!;
            if (connection.Character == null) return;
            
            _isPathfinding = true;
            _target = connection.Character;
            _initialTargetPosition = _target.Movement.Position;
        }
        else
        {
            // Check if target has moved beyond TargetTolerance
            if (Vector3.Distance(_target.Movement.Position, _initialTargetPosition) > TargetTolerance)
            {
                // Reset pathfinding and regenerate path but continue smoothly
                _initialTargetPosition = _target.Movement.Position;

                var currentPosition = Creature.Position;

                // Generate smooth path from NavMesh
                var path = await AStarPathfinding.GeneratePath(
                    currentPosition, 
                    _target.Movement.Position,
                    Chunk.Metadata.NavMesh.Indices,
                    Chunk.Metadata.NavMesh.Vertices,
                    Chunk.Metadata.NavMesh.Areas
                );

                if (path == null)
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
                Console.WriteLine("Reached the end of the path");
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
                Console.WriteLine("Collision detected at: {0}", currentPosition);
                Creature.Velocity = Vector3.zero;
            }
        }
        else
        {
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
