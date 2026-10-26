using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.World;
using Avalon.World.Auras;
using Avalon.World.Characters;
using Avalon.World.ChunkLayouts;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Maps.Navigation;
using Avalon.World.Persistence;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Avalon.World.Scripts;
using Avalon.World.Testing.Fakes;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// What the Awakening meter (#1180 part B) adds to a direct hit: a level 1 warrior's raw hit of 10 on a dummy creature,
/// through a real instance's combat service over the seeded reference data. <see cref="Hit_NoSeal" /> (the baseline): a
/// warrior with no seal worn, whose hit pays one compare of its meter's count. <see cref="Hit_SealedWorn" />: a warrior
/// wearing the sealed kit (<see cref="ScenarioWorld.WearSealedKit" />, a rare amulet at item level 1000, whose bar of
/// 10,040 points is never filled here), whose hit tests the creature, reads its level and rarity for the weight, and adds
/// its share to the meter in 64 bits: no row written, no save mark, no client change, no clock read. 0 B expected on both.
/// </summary>
/// <remarks>
/// <para>
/// The instance draws from a benchmark-local random that always answers 0.15: the hit is not evaded or blocked and does
/// not crit (a level 1 warrior's crit chance is far under 15 %). The dummy's script takes each hit off its health
/// (<see cref="uint.MaxValue" /> at the start), since the meter reads the health a creature lost, and puts it back to
/// <see cref="uint.MaxValue" /> once it falls below half, so it never dies; nothing ticks it or the instance: every
/// iteration measures the same hit on the same unit, both rows paying the same script. Its maximum health is 1, so
/// whatever its armour leaves of a hit is worth at least its weight in points rather than a share of
/// <see cref="uint.MaxValue" /> that rounds to nothing: the meter's add is the same few integer operations either way,
/// and one hit then moves the bar, which the setup checks. The points pile up in the meter for the whole run, in 64 bits
/// (some 10^4 per hit, far from overflowing in any run), and nothing settles them.
/// </para>
/// <para>
/// Setup fails the run unless, judged through public surfaces only, a first <see cref="Hit_SealedWorn" /> followed by a
/// save snapshot (<see cref="CharacterSaveSnapshot.Take" />, which settles the meter first) leaves the sealed row's
/// progress above where it started, in the character's modifier store and in the snapshot's modifier rows; the setup then
/// puts the row's progress back where it was.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class AwakeningMeterBenchmarks
{
    private const uint Damage = 10;

    private readonly FixedRandom _random = new(0.15);
    private readonly TimeProvider _clock = new FixedClock(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero));

    private ServiceProvider _services = null!;
    private MapInstance _instance = null!;
    private ICombatService _combat = null!;
    private CharacterEntity _plain = null!;
    private CharacterEntity _sealed = null!;
    private Creature _dummy = null!;

    [GlobalSetup]
    public void Setup()
    {
        StaticData data = ScenarioReferenceData.Shared.Data;
        var services = new ServiceCollection();
        services.AddSingleton(_clock);
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

        _plain = Warrior(1, character =>
        {
            if (!CharacterStatsRefresh.Apply(character, data, CurrentValues.EnterWorld))
                throw new InvalidOperationException("The seed has no warrior stats");
        });
        ItemInstanceModifier seal = null!;
        _sealed = Warrior(2, character => seal = ScenarioWorld.WearSealedKit(character, data));
        CreatureTemplate template = data.CreatureTemplates.First();
        _dummy = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 1),
            Metadata = template,
            Position = new Vector3(0f, 0f, 2f),
            Level = 1,
            Health = 1,
            CurrentHealth = uint.MaxValue,
            ControlResistance = ControlResistance.Of(template, data.CreatureRarityModifiers),
        };
        _dummy.Script = new WoundScript(_dummy, _instance);
        _instance.AddCreature(_dummy);

        // A first sealed hit, then a save snapshot: the hit's point reaches the store's row and the snapshot's copy of it.
        int before = seal.SealProgress.GetValueOrDefault();
        Hit_SealedWorn();
        var snapshot = CharacterSaveSnapshot.Take(_sealed);
        int? saved = snapshot.Batch.Modifiers?.Modifiers.SingleOrDefault(row => row.Id == seal.Id)?.SealProgress;
        if (!(seal.SealProgress > before) || saved != seal.SealProgress)
        {
            throw new InvalidOperationException(
                $"A sealed hit and a save left the sealed row at {seal.SealProgress?.ToString() ?? "no seal"} in the store and " +
                $"{saved?.ToString() ?? "nothing"} in the snapshot, not above {before} in both.");
        }

        seal.SealProgress = before;
    }

    [Benchmark(Baseline = true)]
    public void Hit_NoSeal() => _combat.ApplyDamage(_plain, _dummy, Damage);

    [Benchmark]
    public void Hit_SealedWorn() => _combat.ApplyDamage(_sealed, _dummy, Damage);

    [GlobalCleanup]
    public void Cleanup()
    {
        // Every row must have measured hits on a living dummy.
        if (_dummy.CurrentHealth == 0)
            throw new InvalidOperationException("The dummy died");

        _instance.Dispose();
        _services.Dispose();
    }

    /// <summary>A level 1 warrior, put in the instance once <paramref name="dress" /> has refreshed its stats with what it wears.</summary>
    private CharacterEntity Warrior(uint id, Action<CharacterEntity> dress)
    {
        var row = new Character
        {
            Id = new CharacterId(id),
            AccountId = new AccountId(1),
            Name = $"Awakening{id}",
            Class = CharacterClass.Warrior,
            Level = 1,
            CreationDate = _clock.GetUtcNow().UtcDateTime,
        };
        var character = new CharacterEntity(NullLoggerFactory.Instance, row, new RegenConfiguration(), _clock) { Data = row };
        dress(character);
        character.Spells.Load([]);
        character.InstanceId = _instance.InstanceId;
        _instance.AddCharacter(new QuietConnection(character));
        return character;
    }

    /// <summary>Takes each hit off the creature's health, and puts it back to full once below half: it never dies.</summary>
    private sealed class WoundScript(ICreature creature, ISimulationContext context) : AiScript(creature, context)
    {
        public override object State { get; set; } = 0;

        public override TimeSpan NextUpdateIn => TimeSpan.MaxValue;

        public override void OnHit(IUnit attacker, uint damage)
        {
            uint left = Creature.CurrentHealth - damage;
            Creature.CurrentHealth = left < uint.MaxValue / 2 ? uint.MaxValue : left;
        }

        protected override bool ShouldRun() => false;
    }

    /// <summary>A clock that never moves.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Answers <paramref name="value" /> to every draw.</summary>
    private sealed class FixedRandom(double value) : ICombatRandom
    {
        public double NextDouble() => value;

        public long NextInt64(long minInclusive, long maxInclusive) => minInclusive;
    }
}
