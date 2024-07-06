using System.Drawing;
using System.Numerics;
using Avalon.Common.Extensions;
using Avalon.Domain.Characters;
using Avalon.World.Entities;
using Avalon.World.Pathfinding;
using MapInstance = Avalon.World.Maps.MapInstance;

namespace Avalon.World.Scripts.Creatures;

public class UrielPathfinderScript : AiScript
{
    private bool _isPathfinding = false;
    private bool _hasWaypoints = false;
    private (int, int)[]? _waypoints;
    private ushort _currentWaypointIndex = 0;
    private Character _target;
    private Vector2 _initialTargetPosition;
    
    private const float WaypointTolerance = 0.1f;
    private const float TargetTolerance = 0.25f;
    
    public UrielPathfinderScript(Creature creature, MapInstance map) : base(creature, map)
    {
        
    }
    
    public override async Task Update(TimeSpan deltaTime)
    {
        if (!_isPathfinding)
        {
            if (Map.Connections.IsEmpty) return;
            var kvp = Map.Connections.FirstOrDefault();
            var connection = kvp.Value;
            if (connection.Character == null) return;
            
            _isPathfinding = true;
            _target = connection.Character;
            _initialTargetPosition = _target.Movement.Position;
        }
        else
        {
            // Check if target has moved beyond TargetTolerance
            if (Vector2.Distance(_target.Movement.Position, _initialTargetPosition) > TargetTolerance)
            {
                // Reset pathfinding and regenerate path but continue smoothly
                _initialTargetPosition = _target.Movement.Position;

                var currentPosition = Creature.Position;

                var newWaypoints = await AStarPathfinding.GeneratePath(
                    (int)currentPosition.X, 
                    (int)currentPosition.Y, 
                    (int)_target.Movement.Position.X, 
                    (int)_target.Movement.Position.Y,
                    Map.VirtualizedMap.WalkableMap
                );

                if (newWaypoints.Length > 0)
                {
                    _waypoints = newWaypoints;
                    _currentWaypointIndex = 1;
                    _hasWaypoints = true;
                }
                else
                {
                    _hasWaypoints = false;
                }
            }

            if (_hasWaypoints && _waypoints != null && _currentWaypointIndex < _waypoints.Length)
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
        var targetPosition = new Vector2(_waypoints![_currentWaypointIndex].Item1, _waypoints[_currentWaypointIndex].Item2);
        var direction = (targetPosition - currentPosition).Normalized();
        var distance = Vector2.Distance(currentPosition, targetPosition);
        
        if (distance <= WaypointTolerance)
        {
            _currentWaypointIndex++;
            if (_currentWaypointIndex >= _waypoints.Length)
            {
                Console.WriteLine("Reached the end of the path");
                Creature.Velocity = Vector2.Zero;
                return;
            }
            targetPosition = new Vector2(_waypoints[_currentWaypointIndex].Item1, _waypoints[_currentWaypointIndex].Item2);
            direction = (targetPosition - currentPosition).Normalized();
            distance = Vector2.Distance(currentPosition, targetPosition);
        }
        
        var movementDelta = direction * Creature.Speed * (float)deltaTime.TotalSeconds;

        if (CheckCollision(movementDelta))
        {
            // Attempt to adjust movement by checking individual axes
            var moveDeltaX = movementDelta with {Y = 0};
            var moveDeltaY = movementDelta with {X = 0};
            
            if (!CheckCollision(moveDeltaX))
            {
                Creature.Position += moveDeltaX;
            }
            else if (!CheckCollision(moveDeltaY))
            {
                Creature.Position += moveDeltaY;
            }
            else
            {
                Console.WriteLine("Collision detected at: {0}, {1}", currentPosition.X, currentPosition.Y);
                Creature.Velocity = Vector2.Zero;
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
        var newPosition = Creature.Position + movementDelta;
        var boundingBox = new Rectangle(newPosition.ToPoint(), Creature.Bounds.Size / 2);
        
        return Map.VirtualizedMap.IsObjectColliding(boundingBox);
    }
}
