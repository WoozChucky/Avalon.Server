using Avalon.Common.Mathematics;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;
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

    public CreatureRangeDetectorScript(ILoggerFactory loggerFactory, ICreature creature, IChunk chunk, float aggroRange) : base(creature, chunk)
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

        var characters = Chunk.Characters.Values;
        foreach (var character in characters)
        {
            var characterPosition = character.Position;
            var distance = Vector3.Distance(Creature.Position, characterPosition);
            if (distance <= _aggroRange)
            {
                if (!Chunk.Navigator.HasVisibility(Creature.Position, characterPosition)) continue;
                State = RangeDetectionState.Detected;
                CharacterDetected?.Invoke(character);
                break;
            }
        }
    }
}
