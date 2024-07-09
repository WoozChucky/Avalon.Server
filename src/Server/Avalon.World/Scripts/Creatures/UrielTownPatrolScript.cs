using Avalon.Common.Mathematics;
using Avalon.World.Entities;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Creatures;

public class UrielTownPatrolScript : AiScript
{
    private readonly ILogger<UrielTownPatrolScript> _logger;
    
    private Vector3[] _waypoints = new Vector3[]
    {
        new Vector3(3, 100.5f, 35),
        new Vector3(25, 100.5f, 35)
    };

    public UrielTownPatrolScript(ILoggerFactory loggerFactory, Creature creature, Chunk chunk) : base(creature, chunk)
    {
        _logger = loggerFactory.CreateLogger<UrielTownPatrolScript>();
    }
    
    public override Task Update(TimeSpan deltaTime)
    {
        // This will simply go back and forth between two points
        
        var currentPosition = Creature.Position;
        var targetPosition = _waypoints[0];
        
        if (Vector3.Distance(currentPosition, targetPosition) < 0.1f)
        {
            _logger.LogInformation("Reached waypoint {Waypoint}", targetPosition);
            _waypoints = _waypoints.Reverse().ToArray();
            targetPosition = _waypoints[0];
        }
        
        var direction = Vector3.Normalize(targetPosition - currentPosition);
        
        Creature.Position += direction * Creature.Speed * (float)deltaTime.TotalSeconds;
        
        return Task.CompletedTask;
    }
}
