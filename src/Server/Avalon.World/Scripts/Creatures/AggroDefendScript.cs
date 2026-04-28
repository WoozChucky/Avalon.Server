using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Creatures;

/// <summary>
/// Generic stationary mob: stands at its spawn, aggros characters that enter its
/// detection range (<see cref="ICreatureMetadata.DetectionRange"/>), and engages
/// via <see cref="CreatureCombatScript"/> until it dies or returns to spawn.
/// Default for procedural-pool creatures — set
/// <see cref="Avalon.Domain.World.CreatureTemplate.ScriptName"/> = "AggroDefendScript".
/// </summary>
public sealed class AggroDefendScript : AiScript
{
    private const float DefaultAggroRange = 10.0f;

    private readonly AiScript _detector;
    private readonly AiScript _combat;

    public AggroDefendScript(ILoggerFactory loggerFactory, ICreature creature, ISimulationContext context)
        : base(creature, context)
    {
        var aggroRange = creature.Metadata.DetectionRange > 0f ? creature.Metadata.DetectionRange : DefaultAggroRange;

        var detector = new CreatureRangeDetectorScript(loggerFactory, creature, context, aggroRange);
        detector.CharacterDetected += OnCharacterEnteredRange;
        _detector = detector;

        _combat = new CreatureCombatScript(loggerFactory, creature, context);

        Chain(_detector);
        Chain(_combat);
    }

    public override object State { get; set; } = string.Empty;
    protected override bool ShouldRun() => true;

    private void OnCharacterEnteredRange(ICharacter character) => _combat.OnEnteredRange(character);

    public override void Update(TimeSpan deltaTime)
    {
        // After combat resolves (target died / left leash), put the detector back into search
        // mode so the creature can re-aggro a fresh threat.
        if (_combat.State is CreatureCombatScript.CombatState.None
            && _detector.State is CreatureRangeDetectorScript.RangeDetectionState.Detected)
        {
            _detector.State = CreatureRangeDetectorScript.RangeDetectionState.Searching;
        }

        base.Update(deltaTime);
    }
}
