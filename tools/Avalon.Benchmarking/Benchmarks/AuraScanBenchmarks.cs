using System.Diagnostics;
using System.Reflection;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.World.Public.Enums;
using Avalon.World.Auras;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// The aura scans of one instance's tick (#898), in a town instance of <see cref="Units" /> characters who all see one
/// another, <see cref="HoldingPercent" /> of them holding a ticking aura (Renew, 12 s, a heal every 3 s, on characters at
/// full health, so a tick restores nothing and sends nothing). Every row is per tick of that one instance:
/// <list type="bullet">
/// <item><see cref="Aura_pass" />: <c>AuraSystem.Update</c> with nothing due, which is the walk that finds the holders
/// and each holder's look at its auras.</item>
/// <item><see cref="Update_scan" />: the broadcast's aura updates to every character with no unit changed, which is
/// only the walk that looks for changed units, once per recipient.</item>
/// <item><see cref="Clear_scan" />: the end of the broadcast, forgetting every unit's changes with none changed.</item>
/// <item><see cref="Tick" />: whole world ticks with the auras ticking and, every second, a tenth of the holders
/// refreshed, so each holder is renewed every 10 s and every refresh is an aura update to every character.</item>
/// </list>
/// </summary>
/// <remarks>
/// The update scan and the clear are private to <c>MapInstance</c>; they are bound here by name (the same names and
/// signature before and after #898), so the rows run the instance's own code with nothing around it. The instance and
/// its characters are real (<see cref="ScenarioWorld.CreateWithReferenceData" />), and so are the sends of
/// <see cref="Tick" />, run inline as the scenario runner does.
/// </remarks>
[MemoryDiagnoser]
public class AuraScanBenchmarks
{
    private const int TicksPerOperation = 60;
    private static readonly AuraId s_renew = new(4);
    private static readonly IReadOnlyList<ObjectGuid> s_noneAdded = [];

    private ScenarioWorld _world = null!;
    private MapInstance _instance = null!;
    private CharacterEntity[] _characters = null!;
    private ScenarioConnection[] _connections = null!;
    private int _holders;
    private int _nextRefresh;
    private Action<IWorldConnection, CharacterEntity, IReadOnlyList<ObjectGuid>> _sendAuraUpdates = null!;
    private Action _clearAuraChanges = null!;

    [Params(50, 100, 200)]
    public int Units { get; set; }

    [Params(10, 100)]
    public int HoldingPercent { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = ScenarioWorld.CreateWithReferenceData();
        _instance = _world.AddInstance();
        _characters = new CharacterEntity[Units];
        _connections = new ScenarioConnection[Units];
        for (int i = 0; i < Units; i++)
        {
            _characters[i] = _world.NewCharacter(CharacterClass.Healer);
            _connections[i] = _world.Join(_instance, _characters[i], new Vector3(i % 4, 0, i % 3));
        }

        _holders = Math.Max(1, Units * HoldingPercent / 100);
        for (int i = 0; i < _holders; i++)
        {
            if (_instance.Auras.Apply(_characters[i], _characters[i], s_renew, AuraSource.None) != AuraApplyResult.Applied)
                throw new InvalidOperationException("Renew was not applied");
        }

        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        _sendAuraUpdates = typeof(MapInstance).GetMethod("SendAuraUpdates", Private)!
            .CreateDelegate<Action<IWorldConnection, CharacterEntity, IReadOnlyList<ObjectGuid>>>(_instance);
        _clearAuraChanges = typeof(MapInstance).GetMethod("ClearAuraChanges", Private)!.CreateDelegate<Action>(_instance);

        var warmup = Stopwatch.StartNew();
        while (warmup.Elapsed < TimeSpan.FromSeconds(5))
            Tick();

        RequireHolders();
    }

    [Benchmark(OperationsPerInvoke = TicksPerOperation)]
    public void Aura_pass()
    {
        for (int i = 0; i < TicksPerOperation; i++)
            _instance.Auras.Update();
    }

    [Benchmark(OperationsPerInvoke = TicksPerOperation)]
    public void Update_scan()
    {
        for (int i = 0; i < TicksPerOperation; i++)
        {
            for (int c = 0; c < _characters.Length; c++)
                _sendAuraUpdates(_connections[c], _characters[c], s_noneAdded);
        }
    }

    [Benchmark(OperationsPerInvoke = TicksPerOperation)]
    public void Clear_scan()
    {
        for (int i = 0; i < TicksPerOperation; i++)
            _clearAuraChanges();
    }

    [Benchmark(OperationsPerInvoke = TicksPerOperation)]
    public void Tick()
    {
        // A tenth of the holders refreshed every 60 ticks, one a tick: each is renewed every 10 s, inside Renew's 12 s.
        int refreshes = (_holders + 9) / 10;
        for (int i = 0; i < TicksPerOperation; i++)
        {
            if (i < refreshes)
            {
                CharacterEntity holder = _characters[_nextRefresh];
                _nextRefresh = (_nextRefresh + 1) % _holders;
                _instance.Auras.Apply(holder, holder, s_renew, AuraSource.None);
            }

            _world.Tick();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        RequireHolders();
        _world.Dispose();
    }

    /// <summary>Every holder still holds its Renew, so no row measured an instance whose auras had run out.</summary>
    private void RequireHolders()
    {
        for (int i = 0; i < _holders; i++)
        {
            if (_characters[i].Auras.Count != 1)
                throw new InvalidOperationException($"Holder {i} holds {_characters[i].Auras.Count} auras");
        }
    }
}
