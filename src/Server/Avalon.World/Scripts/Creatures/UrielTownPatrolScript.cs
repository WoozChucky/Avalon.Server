using Avalon.Common.Mathematics;
using Avalon.World.Entities;
using Avalon.World.Maps;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Creatures;

public class UrielTownPatrolScript : AiScript
{
    private readonly ILogger<UrielTownPatrolScript> _logger;
    
    private Vector3[] _waypoints = new Vector3[]
    {
        new Vector3(28.15f, 50.0f, 90.10f),
        new Vector3(56.65f, 50.0f, 90.03f)
    };
    
    private readonly AiScript _patrolScript;
    private readonly AiScript _idleScript;
    
    private AiScript _currentScript;

    public UrielTownPatrolScript(ILoggerFactory loggerFactory, Creature creature, Chunk chunk) : base(creature, chunk)
    {
        _logger = loggerFactory.CreateLogger<UrielTownPatrolScript>();
        
        _patrolScript = new CreaturePatrolScript(loggerFactory, creature, chunk, _waypoints);
        _patrolScript.State = CreaturePatrolScript.PatrolState.Patrolling;
        
        _idleScript = new CreatureIdleScript(creature, chunk);
        _idleScript.State = false;
        
        _currentScript = _patrolScript;
        
        Chain(_patrolScript);
        Chain(_idleScript);
    }
    
    public override async Task Update(TimeSpan deltaTime)
    {
        switch (_currentScript)
        {
            case CreaturePatrolScript patrolScript:
                if (patrolScript.State is CreaturePatrolScript.PatrolState.Idle)
                {
                    _currentScript = _idleScript;
                    _idleScript.State = true;
                }
                
                break;
            case CreatureIdleScript idleScript:
                if (idleScript.State is false)
                {
                    _currentScript = _patrolScript;
                    _patrolScript.State = CreaturePatrolScript.PatrolState.Patrolling;
                }
                break;
        }
        
        await base.Update(deltaTime); // Will call Update on all chained scripts, and they will run if ShouldRun returns true
    }

    public override object State { get; set; }
    protected override bool ShouldRun()
    {
        return true;
    }
}
