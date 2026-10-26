using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;
using Avalon.World;
using Avalon.World.Auras;
using Avalon.World.ChunkLayouts;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Maps.Navigation;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Enums;
using Avalon.World.Scripts;
using Avalon.World.Testing.Fakes;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// What Resonance effects (#1180 part A2) add to a direct hit: a level 1 warrior's raw hit of 10 on a dummy creature,
/// through a real instance's combat service, its Resonance check and dispatcher, and its aura system, over the seeded
/// reference data. <see cref="Hit_NoResonance" /> (the baseline): a warrior with no theme, whose hit tests its empty mask
/// and reports nothing. <see cref="Hit_ThemedOnCooldown" />: a warrior at Ember 4 and Thorn 4 (the Resonance kit,
/// <see cref="ScenarioWorld.WearResonanceKit" />), whose hit matches Ember's Hit effect (Burning III, 30 %, 2 s) while its
/// cooldown runs: one call to the instance, one clock read and a compare, and no chance drawn.
/// <see cref="Hit_ThemedFiring" />: the same warrior with its cooldown ready again before every hit, so every hit draws
/// the chance, fires, spends the cooldown and renews the dummy's Burning III. 0 B expected on every row.
/// </summary>
/// <remarks>
/// <para>
/// The instance draws from a benchmark-local random that always answers 0.15: the hit is not evaded or blocked and does
/// not crit (a level 1 warrior's crit chance is far under 15 %), and Ember's 30 % fires. Its clock is a benchmark-local
/// one that only moves when told, so the firing row's 2 s step before each hit costs a field write rather than a fake
/// timer provider's lock. The dummy has no script, so a hit leaves its health (<see cref="uint.MaxValue" />) as it is, and
/// nothing ticks it or the instance: every iteration measures the same hit on the same unit.
/// </para>
/// <para>
/// Setup fails the run unless, judged through public surfaces only, a first <see cref="Hit_ThemedFiring" /> leaves the
/// dummy holding Burning III from the warrior, after the hit's three draws and the chance's one; and a
/// <see cref="Hit_ThemedOnCooldown" /> then changes none of the dummy's auras and draws only the hit's three. Every row
/// runs after both, on a dummy holding Burning III, so all three measure the same target.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class ResonanceProcBenchmarks
{
    private const uint Damage = 10;

    // The seeded Burning III, the aura Ember 4's Hit effect places (ResonanceSeed).
    private static readonly AuraId s_burning3 = new(18);

    private static readonly TimeSpan s_cooldown = TimeSpan.FromSeconds(2);

    private readonly SteppedClock _clock = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly FixedRandom _random = new(0.15);

    private ServiceProvider _services = null!;
    private MapInstance _instance = null!;
    private ICombatService _combat = null!;
    private CharacterEntity _plain = null!;
    private CharacterEntity _themed = null!;
    private Creature _dummy = null!;

    [GlobalSetup]
    public void Setup()
    {
        StaticData data = ScenarioReferenceData.Shared.Data;
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton(new CombatConfig());
        services.AddSingleton<IScriptManager>(new ScriptManager(NullLoggerFactory.Instance));
        services.AddSingleton<IChunkLibrary>(new AnyChunkLibrary());
        services.AddSingleton<ICombatRandom>(_random);
        _services = services.BuildServiceProvider();

        var entry = new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero);
        var layout = new ChunkLayout(Seed: 0, Chunks: [entry], EntryChunk: entry, BossChunk: null, Portals: [],
            EntrySpawnWorldPos: Vector3.zero, CellSize: 30f, Config: null);
        _instance = new MapInstance(NullLoggerFactory.Instance, _services, new QuietWorld(data), new MapTemplateId(1),
            ownerCharacterId: null, layout, new MapNavigator(NullLoggerFactory.Instance), seed: 0, mapType: MapType.Normal);
        _combat = _instance.CombatService;

        _plain = Warrior(data, 1);
        _themed = Warrior(data, 2, ("P8", DamageType.Fire), ("S9", null));
        CreatureTemplate template = data.CreatureTemplates.First();
        _dummy = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 1),
            Metadata = template,
            Position = new Vector3(0f, 0f, 2f),
            Level = 1,
            Health = uint.MaxValue,
            CurrentHealth = uint.MaxValue,
            ControlResistance = ControlResistance.Of(template, data.CreatureRarityModifiers),
        };
        _instance.AddCreature(_dummy);

        // A first firing: the hit's three draws and the chance's one, and Burning III on the dummy from the warrior.
        long drawn = _random.Drawn;
        Hit_ThemedFiring();
        if (_random.Drawn - drawn != 4 || !Holds(_dummy, s_burning3, _themed.Guid))
        {
            throw new InvalidOperationException(
                $"The first firing drew {_random.Drawn - drawn} numbers (not 4) and left the dummy holding {Describe(_dummy)}, not Burning III from the warrior.");
        }

        // On cooldown: the hit's three draws only, and the dummy's auras as they were.
        string before = Describe(_dummy);
        drawn = _random.Drawn;
        Hit_ThemedOnCooldown();
        if (_random.Drawn - drawn != 3 || Describe(_dummy) != before)
        {
            throw new InvalidOperationException(
                $"A hit on cooldown drew {_random.Drawn - drawn} numbers (not 3) and left the dummy holding {Describe(_dummy)}, not {before}.");
        }
    }

    [Benchmark(Baseline = true)]
    public void Hit_NoResonance() => _combat.ApplyDamage(_plain, _dummy, Damage);

    [Benchmark]
    public void Hit_ThemedOnCooldown() => _combat.ApplyDamage(_themed, _dummy, Damage);

    [Benchmark]
    public void Hit_ThemedFiring()
    {
        _clock.Now += s_cooldown;
        _combat.ApplyDamage(_themed, _dummy, Damage);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // Every row must have measured hits on a living dummy holding the warrior's Burning III.
        if (_dummy.CurrentHealth == 0 || !Holds(_dummy, s_burning3, _themed.Guid))
            throw new InvalidOperationException($"The dummy ended at {_dummy.CurrentHealth} health holding {Describe(_dummy)}");

        _instance.Dispose();
        _services.Dispose();
    }

    /// <summary>A level 1 warrior wearing the Resonance kit with <paramref name="affixes" />, its stats refreshed, in the instance.</summary>
    private CharacterEntity Warrior(StaticData data, uint id, params (string Family, DamageType? Element)[] affixes)
    {
        var row = new Character
        {
            Id = new CharacterId(id),
            AccountId = new AccountId(1),
            Name = $"Resonance{id}",
            Class = CharacterClass.Warrior,
            Level = 1,
            CreationDate = _clock.Now.UtcDateTime,
        };
        var character = new CharacterEntity(NullLoggerFactory.Instance, row, new RegenConfiguration(), _clock) { Data = row };
        ScenarioWorld.WearResonanceKit(character, data, affixes);
        character.Spells.Load([]);
        character.InstanceId = _instance.InstanceId;
        _instance.AddCharacter(new QuietConnection(character));
        return character;
    }

    private static bool Holds(Creature unit, AuraId aura, ObjectGuid caster)
    {
        foreach (ActiveAura held in unit.Auras.All)
        {
            if (held.Id == aura && held.CasterGuid == caster)
                return true;
        }

        return false;
    }

    /// <summary>Each held aura as its id, caster, stacks, application time and duration: equal exactly when none changed.</summary>
    private static string Describe(Creature unit) => string.Join(", ", unit.Auras.All.Select(a =>
        $"{a.Id.Value} from {a.CasterGuid} x{a.Stacks} at {a.AppliedAt:O} for {a.DurationMs} ms"));

    /// <summary>A clock that stands still until told to move: the firing row's step is a field write.</summary>
    private sealed class SteppedClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Answers <paramref name="value" /> to every draw, and counts them.</summary>
    private sealed class FixedRandom(double value) : ICombatRandom
    {
        public long Drawn { get; private set; }

        public double NextDouble()
        {
            Drawn++;
            return value;
        }

        public long NextInt64(long minInclusive, long maxInclusive) => minInclusive;
    }
}
