using System.Runtime.CompilerServices;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.World.Abilities;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Avalon.World.Scripts;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// <c>InstanceAbilityCastSystem.GetAbility</c> over <see cref="Scripts" /> projectiles in flight (#883). The state
/// broadcast asks it for every projectile each viewer is sent, so one invocation here is one viewer's share: every
/// active projectile looked up once. <see cref="Walk_the_list" /> is the lookup as it was at every size, a walk of
/// the active scripts comparing guids; <see cref="Cast_system_lookup" /> is the cast system's own, which walks up to
/// <see cref="InstanceAbilityCastSystem.WalkedUpTo" /> scripts and answers from its index by guid above that.
/// <see cref="Refill_the_index" /> is what the first lookup after a cast or a removal pays once above that size, for
/// every viewer together: the index emptied and filled again from the list.
/// </summary>
[MemoryDiagnoser]
public class ActiveScriptLookupBenchmarks
{
    private readonly List<Active> _scripts = [];
    private readonly Dictionary<ulong, AbilityScript> _index = [];
    private InstanceAbilityCastSystem _casts = null!;
    private ObjectGuid[] _guids = null!;

    [Params(1, 4, 8, 16, 64)]
    public int Scripts { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        IScriptManager scripts = Substitute.For<IScriptManager>();
        scripts.GetAbilityScript(nameof(FlyingScript)).Returns(typeof(FlyingScript));
        _casts = new InstanceAbilityCastSystem(NullLoggerFactory.Instance, new ServiceCollection().BuildServiceProvider(),
            scripts, Substitute.For<IAbilityArena>());

        IUnit caster = Substitute.For<IUnit>();
        for (int i = 0; i < Scripts; i++)
        {
            var ability = new GameAbility
            {
                AbilityId = new AbilityId((uint)(i + 1)),
                Metadata = new AbilityMetadata { Name = "Bench", ScriptName = nameof(FlyingScript) },
                CooldownTimer = 0f,
                CastTimeTimer = 0f,
            };
            if (!_casts.RunInstant(caster, default, ability))
                throw new InvalidOperationException("A cast was refused.");
        }

        List<IWorldObject> objects = [];
        _casts.Update(TimeSpan.FromSeconds(1d / 60d), objects);
        _scripts.AddRange(objects.Select(script => new Active((AbilityScript)script)));
        _guids = _scripts.Select(active => new ObjectGuid(active.Script.Guid.RawValue)).ToArray();

        if (_scripts.Count != Scripts || Walk_the_list() != Scripts || Cast_system_lookup() != Scripts
            || Refill_the_index() != Scripts)
        {
            throw new InvalidOperationException("Not every active script was found.");
        }
    }

    [Benchmark(Baseline = true)]
    public int Walk_the_list()
    {
        int found = 0;
        foreach (ObjectGuid guid in _guids)
        {
            if (Walk(guid) is not null)
                found++;
        }

        return found;
    }

    /// <summary>GetAbility as it was before #883, called once per lookup as it was.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private AbilityScript? Walk(ObjectGuid guid)
    {
        for (int i = 0; i < _scripts.Count; i++)
        {
            AbilityScript script = _scripts[i].Script;
            if (script.Guid == guid)
                return script;
        }

        return null;
    }

    [Benchmark]
    public int Cast_system_lookup()
    {
        int found = 0;
        foreach (ObjectGuid guid in _guids)
        {
            if (_casts.GetAbility(guid) is not null)
                found++;
        }

        return found;
    }

    [Benchmark]
    public int Refill_the_index()
    {
        _index.Clear();
        _index.EnsureCapacity(_scripts.Count);
        foreach (Active active in _scripts)
        {
            _index.TryAdd(active.Script.Guid.RawValue, active.Script);
        }

        return _index.Count;
    }

    /// <summary>An active script as the cast system holds it: behind the object that also names its caster and cast.</summary>
    private sealed class Active(AbilityScript script)
    {
        public AbilityScript Script { get; } = script;
    }

    /// <summary>A projectile that stays in flight: it keeps its place among the active scripts.</summary>
    public sealed class FlyingScript(IAbility ability, IUnit caster, AbilityAim aim, IAbilityArena arena)
        : AbilityScript(ability, caster, aim)
    {
        public IAbilityArena Arena { get; } = arena;

        public override object State { get; set; } = SpellState.Executing;
        public override Vector3 Position { get; set; }
        public override Vector3 Velocity { get; set; }
        public override Vector3 Orientation { get; set; }
        public override ObjectGuid Guid { get; set; } = null!;

        public override void Prepare() => Guid = new ObjectGuid(ObjectType.SpellProjectile, IObject.GenerateId());

        protected override bool ShouldRun() => true;
    }
}
