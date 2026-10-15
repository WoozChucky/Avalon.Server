using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Instances;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;

namespace Avalon.World.Scripts.Creatures;

public delegate void CharacterDetectedEventHandler(ICharacter character);

/// <summary>
/// Watches for characters entering the creature's aggro range. A component: AggroDefendScript builds
/// it with its own range and chains it, so it cannot be named in a template's ScriptName.
/// </summary>
[ChainedScript]
public class CreatureRangeDetectorScript : AiScript
{
    public enum RangeDetectionState
    {
        Searching,
        Detected,
    }

    public event CharacterDetectedEventHandler? CharacterDetected;

    /// <summary>Seconds between searches. The instance's creature scheduler times a waiting detector against it (#979).</summary>
    internal const float SearchInterval = 1.0f;

    private readonly float _aggroRange;
    private float _searchTimer = 0.0f;

    public CreatureRangeDetectorScript(ICreature creature, ISimulationContext context, float aggroRange) : base(creature, context)
    {
        _aggroRange = aggroRange;
    }

    public override object State { get; set; } = RangeDetectionState.Searching;

    /// <summary>
    /// The next tick. Read only for a creature's own script, which this never is: its owner runs it, and keeps its timer
    /// waiting for it (#979).
    /// </summary>
    public override TimeSpan NextUpdateIn => TimeSpan.Zero;
    protected override bool ShouldRun()
    {
        return State is RangeDetectionState.Searching;
    }

    // The caller may skip the chain only when this is exactly the work Update would do.
    // Leave a due timer untouched: the ordinary Update then adds this tick once and searches.
    internal bool AdvanceWhileWaiting(TimeSpan deltaTime)
    {
        if (State is not RangeDetectionState.Searching)
            return false;

        float next = _searchTimer + (float)deltaTime.TotalSeconds;
        if (next < SearchInterval)
        {
            _searchTimer = next;
            return true;
        }

        return false;
    }

    /// <summary>
    /// The seconds accumulated toward the next search: read while the creature waits for it, and put back where the
    /// per-tick visits would have it before it runs again (#979).
    /// </summary>
    internal float SearchTimer
    {
        get => _searchTimer;
        set => _searchTimer = value;
    }

    /// <summary>
    /// Whether an Update of <paramref name="deltaTime" /> now would search: the detector is searching and the tick brings
    /// its timer to the interval, the same float sum Update makes.
    /// </summary>
    internal bool SearchDueAfter(TimeSpan deltaTime) =>
        State is RangeDetectionState.Searching && !(_searchTimer + (float)deltaTime.TotalSeconds < SearchInterval);

    /// <summary>
    /// Whether this detector's search could find anyone (#982), asked by <paramref name="instance" />'s creature scheduler
    /// of its spatial grid, <paramref name="characters" />: false only when no character of that instance can be within
    /// range (<see cref="SpatialGrid.MayHaveCharacterWithin" />). A detector that searches another context's characters
    /// than that instance's is always answered true.
    /// </summary>
    internal bool CouldDetect(ISimulationContext instance, SpatialGrid characters) =>
        !ReferenceEquals(Context, instance) || characters.MayHaveCharacterWithin(Creature.Position, _aggroRange);

    /// <summary>
    /// A due search ended without walking the characters, where it could find no one (#982): left as Update leaves a
    /// search that found no one, its timer started again from zero and still searching.
    /// </summary>
    internal void SkipSearch() => _searchTimer = 0.0f;

    /// <summary>
    /// The searches this detector ran (walks of the characters), and the distances they measured (one for each living
    /// character walked). For tests and the <c>detection-counts</c> harness (#982); nothing reads them on the tick.
    /// </summary>
    internal long Searches { get; private set; }

    /// <inheritdoc cref="Searches" />
    internal long DistanceChecks { get; private set; }

    public override void Update(TimeSpan deltaTime)
    {
        _searchTimer += (float)deltaTime.TotalSeconds;
        if (_searchTimer < SearchInterval)
            return;

        _searchTimer = 0.0f;
        Searches++;

        // An instance's own dictionary is walked with its struct enumerator: through the interface, every creature's
        // search boxed one (#880). Any other dictionary (a test's) is walked as before.
        if (Context.Characters is Dictionary<ObjectGuid, ICharacter> characters)
        {
            foreach (ICharacter character in characters.Values)
            {
                if (Detects(character))
                    return;
            }

            return;
        }

        foreach (ICharacter character in Context.Characters.Values)
        {
            if (Detects(character))
                return;
        }
    }

    /// <summary>Whether <paramref name="character" /> is the one detected: then the state and the event are set.</summary>
    private bool Detects(ICharacter character)
    {
        // Don't aggro on dead characters — they're stuck in the respawn modal until they
        // click the button or force-quit, and the combat handlers already drop their
        // input/attack packets. Aggroing would just queue a kill on a corpse.
        if (character.IsDead) return false;

        DistanceChecks++;
        Vector3 characterPosition = character.Position;
        float distance = Vector3.Distance(Creature.Position, characterPosition);
        if (distance > _aggroRange) return false;

        if (!Context.GetNavigatorForPosition(Creature.Position).HasVisibility(Creature.Position, characterPosition)) return false;
        State = RangeDetectionState.Detected;
        CharacterDetected?.Invoke(character);
        return true;
    }
}
