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
/// Named as it is, it fights with no abilities (#163), so a creature on it never attacks: each creature
/// type's own script subclasses it and hands it a combat script carrying its abilities and rotation.
/// </summary>
public class AggroDefendScript : AiScript, IReturningHome
{
    private const float DefaultAggroRange = 10.0f;

    private readonly AiScript _detector;
    private readonly AiScript _combat;

    public AggroDefendScript(ILoggerFactory loggerFactory, ICreature creature, ISimulationContext context,
        TimeProvider? time = null)
        : this(loggerFactory, creature, context, new CreatureCombatScript(loggerFactory, creature, context, time))
    {
    }

    /// <summary>
    /// Aggro detection chained to <paramref name="combat" />, a creature type's own combat script (#163), which
    /// must be built for this same creature and context.
    /// </summary>
    protected AggroDefendScript(ILoggerFactory loggerFactory, ICreature creature, ISimulationContext context,
        CreatureCombatScript combat)
        : base(creature, context)
    {
        float aggroRange = creature.Metadata.DetectionRange > 0f ? creature.Metadata.DetectionRange : DefaultAggroRange;

        var detector = new CreatureRangeDetectorScript(loggerFactory, creature, context, aggroRange);
        detector.CharacterDetected += OnCharacterEnteredRange;
        _detector = detector;

        _combat = combat;

        Chain(_detector);
        Chain(_combat);
    }

    public override object State { get; set; } = string.Empty;
    protected override bool ShouldRun() => true;

    bool IReturningHome.IsReturningHome => _combat is IReturningHome { IsReturningHome: true };

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
