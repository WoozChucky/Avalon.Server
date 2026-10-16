using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.Server.World.UnitTests.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Maps;
using Avalon.World.Public.Enums;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Scripts;

/// <summary>
/// Run alone: a test elsewhere that scans the loaded assemblies for scripts (any that loads a ScriptManager or builds a
/// world host) holds every assembly in the process while it scans, a replaced generation among them, which would be
/// found alive.
/// </summary>
[CollectionDefinition(nameof(ScriptGenerationsShould), DisableParallelization = true)]
public sealed class ScriptGenerationsCollection;

/// <summary>
/// Each compile of the hot-reload scripts is a generation, loaded into a collectible context of its own (#930). The
/// generation that replaces it is the only one the world, the manager and the reloader name, a name only an older one
/// had included, so a replaced one leaves memory once its scripts stop running, and an older or unrelated copy of a
/// name is never picked over the newest; a script that would keep its generation in memory is warned about.
/// Creature ids 930_0xx are unique to this class.
/// </summary>
[Collection(nameof(ScriptGenerationsShould))]
public class ScriptGenerationsShould
{
    private const string ScriptName = "GenerationProbeScript";

    // A script only the first generation has, as if its file were deleted before the second compile.
    private const string RetiredName = "RetiredProbeScript";

    private static readonly TimeSpan s_tick = TimeSpan.FromSeconds(1d / 60d);
    private static readonly MapTemplateId s_townId = new(1);

    /// <summary>Hands the reloader the compiles a test makes, as the file watcher's compiler would.</summary>
    private sealed class HandCompiler : IScriptCompiler
    {
        public event ScriptCompiledEventHandler? ScriptCompiled;

        public void Start() { }

        public void Stop() { }

        public void Raise(CompiledScripts scripts) => ScriptCompiled?.Invoke(scripts);
    }

    /// <summary>The hot reloader, its compiler and a loaded script manager, wired as the world server wires them.</summary>
    private static (HandCompiler Compiler, ScriptHotReloader Reloader, ScriptManager Scripts) HotReload()
    {
        var compiler = new HandCompiler();
        var reloader = new ScriptHotReloader(NullLoggerFactory.Instance, compiler);
        var scripts = new ScriptManager(NullLoggerFactory.Instance);
        scripts.Load();
        reloader.ScriptsHotReloaded += scripts.RegisterHotReloaded;
        reloader.Start();
        return (compiler, reloader, scripts);
    }

    private static CompiledScripts Compile(int generation)
    {
        IEnumerable<string> names = generation == 1 ? [ScriptName, RetiredName] : [ScriptName];
        return ScriptCompiler.Compile(names.Select(name => $$"""
            using Avalon.World.Public.Instances;
            using Avalon.World.Public.Scripts;

            public sealed class {{name}}(ICreature creature, ISimulationContext context) : AiScript(creature, context)
            {
                public const int Generation = {{generation}};

                public override object State { get; set; } = 0;

                public override TimeSpan NextUpdateIn => TimeSpan.Zero;

                protected override bool ShouldRun() => false;
            }
            """), NullLogger.Instance) ?? throw new InvalidOperationException("the probe scripts did not compile");
    }

    [Fact]
    public async Task Let_a_replaced_generation_be_collected_once_its_scripts_are_replaced()
    {
        (HandCompiler compiler, ScriptHotReloader reloader, ScriptManager scripts) = HotReload();
        using MapInstance town = TestMapInstances.Build(NewWorld(), mapType: MapType.Town);
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 930_001),
            Metadata = LootTestData.BoarTemplate(null),
            ScriptName = ScriptName,
            Position = Vector3.zero,
            Health = 100,
            CurrentHealth = 100,
        };
        town.AddCreature(creature);
        Avalon.World.World world = await WorldHolding(town, reloader);

        WeakReference first = ReloadAndRun(compiler, world, scripts, creature, generation: 1);
        WeakReference second = ReloadAndRun(compiler, world, scripts, creature, generation: 2);

        for (int i = 0; i < 20 && first.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(first.IsAlive);
        Assert.True(second.IsAlive); // the creature runs it, and the manager names it
        Assert.Null(scripts.GetAiScript(RetiredName));
    }

    [Fact]
    public void Name_the_newest_generation_over_older_and_unrelated_copies()
    {
        (HandCompiler compiler, ScriptHotReloader reloader, ScriptManager scripts) = HotReload();
        CompiledScripts unrelated = Compile(generation: 0); // loaded at runtime, never handed to the reloader
        CompiledScripts first = Compile(generation: 1);
        CompiledScripts second = Compile(generation: 2);
        try
        {
            Assert.Null(scripts.GetAiScript(ScriptName));

            compiler.Raise(first);
            compiler.Raise(second);
            scripts.Load(); // a load after a reload keeps the generation

            Type newest = second.Assembly.GetType(ScriptName)!;
            Assert.Same(newest, scripts.GetAiScript(ScriptName));
            reloader.Update(out List<Type> handedToTheWorld);
            Assert.Same(newest, Assert.Single(handedToTheWorld));
        }
        finally
        {
            unrelated.Context.Unload();
            second.Context.Unload();
        }
    }

    [Theory]
    [InlineData("ILogger<PinProbeScript> logger", "PinProbeScript")] // the container keeps ILogger<T>, and the compile
    [InlineData("ILogger<PinProbeHelper> logger", "PinProbeHelper")] // any type of the compile pins it alike
    [InlineData("ILoggerFactory loggerFactory", null)]
    public void Warn_once_about_a_script_that_takes_a_service_closed_over_a_type_of_its_compile(string parameter,
        string? closedOver)
    {
        var compiler = new HandCompiler();
        var logs = new RecordingLoggerFactory();
        var reloader = new ScriptHotReloader(logs, compiler);
        reloader.Start();
        CompiledScripts compiled = ScriptCompiler.Compile([$$"""
            using Avalon.World.Public.Instances;
            using Avalon.World.Public.Scripts;

            public sealed class PinProbeHelper;

            public sealed class PinProbeScript(ICreature creature, ISimulationContext context, {{parameter}})
                : AiScript(creature, context)
            {
                public override object State { get; set; } = 0;

                public override TimeSpan NextUpdateIn => TimeSpan.Zero;

                protected override bool ShouldRun() => false;
            }
            """], NullLogger.Instance) ?? throw new InvalidOperationException("the probe script did not compile");
        try
        {
            compiler.Raise(compiled);

            List<string> warnings = [.. logs.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message)];
            if (closedOver is not null)
            {
                string warning = Assert.Single(warnings);
                Assert.Contains("PinProbeScript", warning, StringComparison.Ordinal);
                Assert.Contains(closedOver, warning, StringComparison.Ordinal);
            }
            else
            {
                Assert.Empty(warnings);
            }
        }
        finally
        {
            reloader.Stop();
            compiled.Context.Unload();
        }
    }

    private sealed class RecordingLoggerFactory : ILoggerFactory, ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    /// <summary>A loaded world whose one town instance is <paramref name="town" />, polling <paramref name="reloader" />.</summary>
    private static async Task<Avalon.World.World> WorldHolding(MapInstance town, IScriptHotReloader reloader)
    {
        IAvalonMapManager mapManager = Substitute.For<IAvalonMapManager>();
        mapManager.Templates.Returns([new MapTemplate { Id = s_townId, MapType = MapType.Town }]);
        IChunkLayoutInstanceFactory factory = Substitute.For<IChunkLayoutInstanceFactory>();
        factory.BuildAsync(default!, default, default).ReturnsForAnyArgs(town);

        Avalon.World.World world = await ScriptHotReloadPollingShould.BuildWorldAsync(
            reloader, intervalSeconds: 1, mapManager, factory);
        await world.InstanceRegistry.GetOrCreateTownInstanceAsync(s_townId, maxPlayers: 100).Published(world);
        return world;
    }

    /// <summary>
    /// Compiles and reloads <paramref name="generation" />, lets the world swap it in and run it, and returns its context
    /// weakly. Not inlined, so nothing of the generation is left on the caller's stack.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ReloadAndRun(HandCompiler compiler, Avalon.World.World world, ScriptManager scripts,
        Creature creature, int generation)
    {
        CompiledScripts compiled = Compile(generation);
        Type scriptType = compiled.Assembly.GetType(ScriptName)!;

        compiler.Raise(compiled);
        world.Update(TimeSpan.FromSeconds(1)); // polls the reloader
        world.Update(s_tick);                    // swaps the creature's script, then runs the instance

        Assert.Same(compiled.Context, AssemblyLoadContext.GetLoadContext(scriptType.Assembly));
        Assert.Same(scriptType, scripts.GetAiScript(ScriptName));
        Assert.Same(scriptType, creature.Script?.GetType());
        return new WeakReference(compiled.Context, trackResurrection: true);
    }
}
