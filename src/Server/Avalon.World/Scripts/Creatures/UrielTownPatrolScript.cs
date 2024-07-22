using Avalon.Common.Mathematics;
using Avalon.World.Maps;
using Avalon.World.Public;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Scripts;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Creatures;

public class UrielTownPatrolScript : AiScript
{
    private readonly ILogger<UrielTownPatrolScript> _logger;

    private const float IdleTime = 5.0f;
    
    private readonly Vector3[] _waypoints =
    [
        new Vector3(28.15f, 50.0f, 90.10f),
        new Vector3(56.65f, 50.0f, 90.03f),
        new Vector3(64.01f, 50.0f, 72.14f),
        new Vector3(45.85f, 50.0f, 58.63f),
        new Vector3(28.15f, 50.0f, 90.10f)
    ];
    
    private readonly AiScript _patrolScript;
    private readonly AiScript _idleScript;
    private readonly AiScript _combatScript;
    
    private AiScript _currentScript;

    public UrielTownPatrolScript(ILoggerFactory loggerFactory, ICreature creature, Chunk chunk) : base(creature, chunk)
    {
        _logger = loggerFactory.CreateLogger<UrielTownPatrolScript>();
        
        _patrolScript = new CreaturePatrolScript(loggerFactory, creature, chunk, _waypoints);
        _patrolScript.State = CreaturePatrolScript.PatrolState.Patrolling;
        
        _idleScript = new CreatureIdleScript(creature, chunk, IdleTime);
        _idleScript.State = false;
        
        _combatScript = new CreatureCombatScript(loggerFactory, creature, chunk);
        _combatScript.State = CreatureCombatScript.CombatState.None;
        
        _currentScript = _patrolScript;
        
        Chain(_patrolScript);
        Chain(_idleScript);
        Chain(_combatScript);
    }
    
    public override object State { get; set; }
    protected override bool ShouldRun()
    {
        return true;
    }
    
    public override void Update(TimeSpan deltaTime)
    {
        switch (_currentScript)
        {
            case CreaturePatrolScript patrolScript:
                if (patrolScript.State is CreaturePatrolScript.PatrolState.Idle && _combatScript.State is CreatureCombatScript.CombatState.None)
                {
                    _currentScript = _idleScript;
                    _idleScript.State = true;
                }
                break;
            
            case CreatureIdleScript idleScript:
                if (idleScript.State is false && _combatScript.State is CreatureCombatScript.CombatState.None)
                {
                    _currentScript = _patrolScript;
                    _patrolScript.State = CreaturePatrolScript.PatrolState.Patrolling;
                }
                break;
                
            case CreatureCombatScript combatScript:
                _logger.LogInformation("Combat state: {State}", combatScript.State);
                if (combatScript.State is CreatureCombatScript.CombatState.None)
                {
                    _currentScript = _patrolScript;
                    _patrolScript.State = CreaturePatrolScript.PatrolState.Patrolling;
                }
                break;
        }
        
        base.Update(deltaTime); // Will call Update on all chained scripts, and they will run if ShouldRun returns true
    }
}
