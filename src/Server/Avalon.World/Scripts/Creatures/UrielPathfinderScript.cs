using Avalon.Common.Mathematics;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Creatures;

public class UrielPathfinderScript : AiScript
{
    private readonly ILogger<UrielPathfinderScript> _logger;
    
    private readonly Vector3[] _waypoints =
    [
        new Vector3(91, 50, 93.51f),
        new Vector3(92.49f, 50, 62.89f),
        new Vector3(92.40f, 50, 24.23f),
    ];
    
    private readonly AiScript _detectorScript;
    private readonly AiScript _combatScript;
    private readonly AiScript _patrolScript;
    
    private const float AggroRange = 10.0f;
    
    public UrielPathfinderScript(ILoggerFactory loggerFactory, ICreature creature, IChunk chunk) : base(creature, chunk)
    {
        _logger = loggerFactory.CreateLogger<UrielPathfinderScript>();
        
        var detectorScript = new CreatureRangeDetectorScript(loggerFactory, creature, chunk, AggroRange);
        detectorScript.CharacterDetected += OnCharacterEnteredRange;
        _detectorScript = detectorScript;
        
        _combatScript = new CreatureCombatScript(loggerFactory, creature, chunk);
        
        _patrolScript = new CreaturePatrolScript(loggerFactory, creature, chunk, _waypoints);
        _patrolScript.State = CreaturePatrolScript.PatrolState.Patrolling;
        
        Chain(_detectorScript);
        Chain(_combatScript);
        Chain(_patrolScript);
    }
    
    public override object State { get; set; }
    protected override bool ShouldRun()
    {
        return true;
    }

    private void OnCharacterEnteredRange(ICharacter character)
    {
        _combatScript.OnEnteredRange(character);
    }

    public override void Update(TimeSpan deltaTime)
    {
        // If the creature is not in combat and the detector is in detected state, it means the combat was over and the creature should keep searching
        if (_combatScript.State is CreatureCombatScript.CombatState.None 
            && _detectorScript.State is CreatureRangeDetectorScript.RangeDetectionState.Detected)
        {
            _detectorScript.State = CreatureRangeDetectorScript.RangeDetectionState.Searching;
        }

        // If the creature is in combat, then stop patrolling
        if (_combatScript.State is not CreatureCombatScript.CombatState.None)
        {
            _patrolScript.State = CreaturePatrolScript.PatrolState.Idle;
        }
        
        // If the creature is idle (or reached a waypoint), not in combat, and the detector is searching, then keep patrolling
        if (_patrolScript.State is CreaturePatrolScript.PatrolState.Idle 
            && _combatScript.State is CreatureCombatScript.CombatState.None
            && _detectorScript.State is CreatureRangeDetectorScript.RangeDetectionState.Searching)
        {
            _patrolScript.State = CreaturePatrolScript.PatrolState.Patrolling;
        }
        
        base.Update(deltaTime);
    }
}
