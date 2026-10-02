using Avalon.Combat;
using Avalon.Common;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Combat;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World;
using Avalon.World.Auras;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Units;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Auras;

/// <summary>
/// An aura system over units the test places, a real combat service (no roll ever procs unless a test scripts one)
/// whose outcomes a substitute records, and a test clock. Its catalog holds whatever <see cref="Use" /> was given; its
/// reference data is empty unless a test sets <see cref="Data" />, so a character's stats refresh keeps its maximums.
/// </summary>
internal sealed class AuraHarness
{
    public static readonly DateTimeOffset T0 = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private AuraCatalog _catalog = AuraCatalog.Empty;

    public AuraHarness(int maxPerUnit = 32, ICombatRandom? random = null, AuraScripts? scripts = null,
        Func<IUnit, bool>? returningHome = null)
    {
        Combat = new CombatService(new CombatConfig(), new EncounterRegistry(new CombatConfig(), Time), outcomes: Outcomes,
            time: Time, random: random ?? ScriptedCombatRandom.Plain(1000));
        Auras = new AuraSystem(Combat, Characters, Creatures, () => _catalog, () => Data, Time, maxPerUnit,
            NullLogger.Instance, scripts, connectionOf: null, returningHome);
    }

    public Dictionary<ObjectGuid, ICharacter> Characters { get; } = [];
    public Dictionary<ObjectGuid, ICreature> Creatures { get; } = [];
    public FakeTimeProvider Time { get; } = new(T0);
    public ICombatOutcomes Outcomes { get; } = Substitute.For<ICombatOutcomes>();
    public CombatService Combat { get; }
    public AuraSystem Auras { get; }

    // Loaded over repositories that answer at once, so waiting on it blocks nothing.
    public StaticData Data { get; set; } = TestStaticData.LoadAsync().GetAwaiter().GetResult();

    /// <summary>The aura catalog from now on; the three test scripts are its loaded scripts.</summary>
    public void Use(params AuraTemplate[] templates) =>
        _catalog = new AuraCatalog(templates, name => name switch
        {
            nameof(RecordingAuraScript) => typeof(RecordingAuraScript),
            nameof(ThrowingAuraScript) => typeof(ThrowingAuraScript),
            nameof(EndOnTickAuraScript) => typeof(EndOnTickAuraScript),
            _ => null,
        }, NullLoggerFactory.Instance);

    public CharacterEntity Player(uint id, uint health = 500)
    {
        CharacterEntity character = TestCharacters.New(id);
        character.Health = health;
        character.CurrentHealth = health;
        Characters[character.Guid] = character;
        return character;
    }

    public Creature Creature(uint id, uint health = 1000, uint armor = 0)
    {
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id), Level = 1, Health = health, CurrentHealth = health,
            BaseMaxHealth = health, Armor = armor, Speed = 4f,
        };
        Creatures[creature.Guid] = creature;
        return creature;
    }

    public void Advance(TimeSpan by) => Time.Advance(by);
}
