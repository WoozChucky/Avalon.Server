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
/// <remarks>
/// Updated only when due (#979), but for a subclass built through the protected constructor, which keeps every tick
/// (#916): while it fights,
/// returns home or re-arms its detector it runs every tick; while it only waits for its next search, the instance keeps
/// its detector's timer (<see cref="IAwaitsSearch" />) and runs it on the tick that search is due, or after a hit, a
/// dodge or a character leaving.
/// </remarks>
public class AggroDefendScript : AiScript, IReturningHome, IAwaitsSearch
{
    private const float DefaultAggroRange = 10.0f;

    private readonly CreatureRangeDetectorScript _detector;
    private readonly AiScript _combat;
    private readonly bool _idleTimerOnly;

    public AggroDefendScript(ILoggerFactory loggerFactory, ICreature creature, ISimulationContext context,
        TimeProvider? time = null)
        : this(creature, context, new CreatureCombatScript(loggerFactory, creature, context, time))
    {
    }

    /// <summary>
    /// Aggro detection chained to <paramref name="combat" />, a creature type's own combat script (#163), which
    /// must be built for this same creature and context.
    /// </summary>
    protected AggroDefendScript(ICreature creature, ISimulationContext context, CreatureCombatScript combat)
        : this(creature, context, combat, idleTimerOnly: false)
    {
    }

    // Only sealed built-in scripts with the original pair and no custom idle combat predicate may opt in.
    // External extensions use the protected constructor and keep every virtual chain callback.
    private protected AggroDefendScript(ICreature creature, ISimulationContext context, CreatureCombatScript combat,
        bool idleTimerOnly)
        : base(creature, context)
    {
        float aggroRange = creature.Metadata.DetectionRange > 0f ? creature.Metadata.DetectionRange : DefaultAggroRange;

        var detector = new CreatureRangeDetectorScript(creature, context, aggroRange);
        detector.CharacterDetected += OnCharacterEnteredRange;
        _detector = detector;

        _combat = combat;
        _idleTimerOnly = idleTimerOnly || GetType() == typeof(AggroDefendScript);

        Chain(_detector);
        Chain(_combat);
    }

    public override object State { get; set; } = string.Empty;
    protected override bool ShouldRun() => true;

    /// <summary>
    /// The plain script and the sealed built-in ones, the same ones whose idle visits only advance the detector's timer
    /// (#916): a subclass outside this assembly keeps its chain's every-tick callbacks.
    /// </summary>
    public override bool UpdatesWhenDue => _idleTimerOnly;

    /// <summary>Until the next search while it only waits for it; otherwise the next tick.</summary>
    public override TimeSpan NextUpdateIn => ((IAwaitsSearch)this).AwaitsSearch(out float elapsed)
        ? TimeSpan.FromSeconds(CreatureRangeDetectorScript.SearchInterval - elapsed)
        : TimeSpan.Zero;

    // Exactly the state in which Update's idle shortcut below only advances the detector's timer until it is due.
    bool IAwaitsSearch.AwaitsSearch(out float elapsed)
    {
        elapsed = _detector.SearchTimer;
        return _idleTimerOnly && _combat.State is CreatureCombatScript.CombatState.None
                              && _detector.State is CreatureRangeDetectorScript.RangeDetectionState.Searching;
    }

    void IAwaitsSearch.ResumeSearchWait(float elapsed) => _detector.SearchTimer = elapsed;

    bool IReturningHome.IsReturningHome => _combat is IReturningHome { IsReturningHome: true };

    private void OnCharacterEnteredRange(ICharacter character) => _combat.OnEnteredRange(character);

    public override void Update(TimeSpan deltaTime)
    {
        // Idle detection already runs once per second. Between searches only its timer changes.
        // Hits and dodges change combat state immediately, so they bypass this wait on the next tick.
        if (_idleTimerOnly && _combat.State is CreatureCombatScript.CombatState.None
            && _detector.AdvanceWhileWaiting(deltaTime))
        {
            return;
        }

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
