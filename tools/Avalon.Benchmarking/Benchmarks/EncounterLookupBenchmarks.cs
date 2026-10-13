using Avalon.Common;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Units;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// <c>EncounterRegistry.FindEncounterContaining</c> walks an instance's active encounters and asks each whether it
/// holds the unit (#883): is the walk worth an index by unit? Each encounter here is a pack of four hostiles and a
/// party of three. <see cref="Find_in_the_last_encounter" /> is the longest walk that finds, <see cref="Find_in_none" />
/// the walk that finds nothing, and <see cref="Indexed" /> is the least an index could cost: one lookup in a dictionary
/// by unit, which the registry would have to keep right through every way an encounter gains or loses a member.
/// </summary>
/// <remarks>
/// An instance's encounters are the fights going on in it at once: one for a party fighting a pack, a few when its
/// members fight apart. The registry is per instance, so the count never adds up across instances.
/// </remarks>
[MemoryDiagnoser]
public class EncounterLookupBenchmarks
{
    private EncounterRegistry _registry = null!;
    private Dictionary<IUnit, IEncounter> _index = null!;
    private IUnit _inTheLast = null!;
    private IUnit _inNone = null!;

    [Params(1, 2, 4, 8, 32)]
    public int Encounters { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _registry = new EncounterRegistry(new CombatConfig());
        _index = [];
        uint next = 1;
        for (int e = 0; e < Encounters; e++)
        {
            var encounter = (Encounter)_registry.CreateEncounter();
            for (int i = 0; i < 3; i++)
            {
                IUnit player = Unit(next++);
                encounter.AddPlayer(player);
                _index[player] = encounter;
            }

            for (int i = 0; i < 4; i++)
            {
                IUnit hostile = Unit(next++);
                encounter.AddHostile(hostile);
                _index[hostile] = encounter;
                _inTheLast = hostile;
            }
        }

        _inNone = Unit(next);
        if (Find_in_the_last_encounter() is null || Find_in_none() is not null || Indexed() is null)
            throw new InvalidOperationException("The lookups do not answer as set up.");
    }

    [Benchmark(Baseline = true)]
    public IEncounter? Find_in_the_last_encounter() => _registry.FindEncounterContaining(_inTheLast);

    [Benchmark]
    public IEncounter? Find_in_none() => _registry.FindEncounterContaining(_inNone);

    [Benchmark]
    public IEncounter? Indexed() => _index.GetValueOrDefault(_inTheLast);

    private static Creature Unit(uint id) => new() { Guid = new ObjectGuid(ObjectType.Creature, id) };
}
