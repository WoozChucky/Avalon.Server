using Avalon.Common.Mathematics;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Creatures;

public delegate void CharacterDetectedEventHandler(ICharacter character);

public class CreatureRangeDetectorScript : AiScript
{
    public enum RangeDetectionState
    {
        Searching,
        Detected,
    }

    public event CharacterDetectedEventHandler? CharacterDetected;

    private readonly ILogger<CreatureRangeDetectorScript> _logger;
    private readonly float _aggroRange;
    private const float SearchInterval = 1.0f;
    private float _searchTimer = 0.0f;

    public CreatureRangeDetectorScript(ILoggerFactory loggerFactory, ICreature creature, ISimulationContext context, float aggroRange) : base(creature, context)
    {
        _logger = loggerFactory.CreateLogger<CreatureRangeDetectorScript>();
        _aggroRange = aggroRange;
    }

    public override object State { get; set; } = RangeDetectionState.Searching;
    protected override bool ShouldRun()
    {
        return State is RangeDetectionState.Searching;
    }

    public override void Update(TimeSpan deltaTime)
    {
        _searchTimer += (float)deltaTime.TotalSeconds;
        if (_searchTimer < SearchInterval)
            return;

        _searchTimer = 0.0f;

        var characters = Context.Characters.Values;
        foreach (var character in characters)
        {
            // Don't aggro on dead characters — they're stuck in the respawn modal until they
            // click the button or force-quit, and the combat handlers already drop their
            // input/attack packets. Aggroing would just queue a kill on a corpse.
            if (character.IsDead) continue;

            var characterPosition = character.Position;
            var distance = Vector3.Distance(Creature.Position, characterPosition);
            if (distance <= _aggroRange)
            {
                if (!Context.GetNavigatorForPosition(Creature.Position).HasVisibility(Creature.Position, characterPosition)) continue;
                State = RangeDetectionState.Detected;
                CharacterDetected?.Invoke(character);
                break;
            }
        }
    }
}
