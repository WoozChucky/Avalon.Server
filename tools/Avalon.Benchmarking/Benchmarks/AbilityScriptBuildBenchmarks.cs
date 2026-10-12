using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.World.Abilities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Avalon.World.Scripts.Abilities;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// What building one cast's ability script costs (#925), as <c>InstanceAbilityCastSystem.Build</c> does it for every
/// cast, a player's or a creature's: the ability's clone, then the script built from (ability, caster, aim, arena).
/// <see cref="Activated_per_cast" /> is the call the cast system made before #925,
/// <c>ActivatorUtilities.CreateInstance</c> with its <c>params</c> array, and <see cref="Cached_factory" /> the
/// <see cref="AbilityScriptActivator" /> it calls now. The other two are the parts no activation can avoid, to read
/// what is left against: the clone alone, and the script built with <c>new</c> over a clone made beforehand.
/// </summary>
[MemoryDiagnoser]
public class AbilityScriptBuildBenchmarks
{
    public enum Script
    {
        Circle,
        Projectile,
    }

    private readonly AbilityScriptActivator _activator = new();
    private readonly AbilityAim _aim = new(new Vector3(0f, 0f, 1f), new Vector3(3f, 0f, 4f)) { Origin = Vector3.zero };
    private ServiceProvider _services = null!;
    private GameAbility _ability = null!;
    private IAbility _clone = null!;
    private IUnit _caster = null!;
    private IAbilityArena _arena = null!;
    private Type _type = null!;

    /// <summary>A circle (a cone is the same size) or a projectile, which also keeps the set of units it has hit.</summary>
    [Params(Script.Circle, Script.Projectile)]
    public Script Shape { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _services = new ServiceCollection().BuildServiceProvider();
        _type = Shape == Script.Circle ? typeof(CircleAbilityScript) : typeof(ProjectileAbilityScript);
        _ability = new GameAbility
        {
            AbilityId = new AbilityId(210),
            Metadata = new AbilityMetadata { Name = "Bench", ScriptName = _type.Name, Cooldown = 1.5f, Reach = 20f },
            CooldownTimer = 0f,
            CastTimeTimer = 0f,
        };
        _clone = _ability.Clone();
        _caster = Substitute.For<IUnit>();
        _arena = Substitute.For<IAbilityArena>();

        if (Activated_per_cast()?.GetType() != _type || Cached_factory()?.GetType() != _type
            || Constructor_alone().GetType() != _type)
        {
            throw new InvalidOperationException($"A path did not build a {_type.Name}.");
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _services.Dispose();

    [Benchmark(Baseline = true)]
    public AbilityScript? Activated_per_cast() =>
        ActivatorUtilities.CreateInstance(_services, _type, _ability.Clone(), _caster, _aim, _arena) as AbilityScript;

    [Benchmark]
    public AbilityScript? Cached_factory() =>
        _activator.Build(_services, _type, _ability.Clone(), _caster, _aim, _arena);

    [Benchmark]
    public IAbility Clone_alone() => _ability.Clone();

    [Benchmark]
    public AbilityScript Constructor_alone() => Shape == Script.Circle
        ? new CircleAbilityScript(_clone, _caster, _aim, _arena)
        : new ProjectileAbilityScript(_clone, _caster, _aim, _arena);
}
