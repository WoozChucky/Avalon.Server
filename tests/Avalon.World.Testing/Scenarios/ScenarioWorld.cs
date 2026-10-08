using System.Diagnostics.Metrics;
using Avalon.Combat;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Maps;
using Avalon.World.Maps.Navigation;
using Avalon.World.Parties;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Pvp;
using Avalon.World.Scripts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Avalon.World.Testing.Scenarios;

/// <summary>
/// A world of real <see cref="MapInstance" />s over the shared town navmesh, held by a real
/// <see cref="InstanceRegistry" /> and ticked by a real <see cref="InstanceTicker" />, as <c>World.Update</c> ticks them,
/// with every player behind a <see cref="ScenarioConnection" />. Deterministic: everything the tick reads is real or
/// hand-written, never a substitute, and times by <see cref="Clock" /> and rolls by a seeded random, never the
/// system clock or <see cref="Random.Shared" />.
/// </summary>
/// <remarks>
/// What an instance resolves from the container, and what it gets here: <see cref="TimeProvider" /> (the
/// <see cref="Clock" />), <see cref="CombatConfig" /> (the defaults, as production), <see cref="IScriptManager" />
/// (a real <see cref="ScriptManager" />, not loaded: a cast's script lookup finds nothing, and only a cast looks),
/// <see cref="ICombatRandom" /> (a <see cref="CombatRandom" /> over a seeded <see cref="Random" />) and
/// <see cref="PvpToggle" /> (the real one, on the clock). Left out, so the instance takes its own fallback: the aura
/// scripts, the periodic save, the loot roller and allocator, quests and parties.
/// </remarks>
public sealed class ScenarioWorld : IDisposable
{
    /// <summary>One tick at 60 Hz, as <c>WorldServer</c> runs it.</summary>
    public static readonly TimeSpan Dt = TimeSpan.FromSeconds(1d / 60d);

    private static readonly DateTimeOffset s_start = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly ScenarioWorldHost _world;
    private readonly ServiceProvider _services;
    private readonly List<MapTemplate> _templates = [];
    private readonly ScenarioInstanceFactory _factory = new();
    private readonly InstanceRegistry _registry;
    private readonly Meter _meter = new("scenario");
    private readonly InstanceTicker _ticker;
    private readonly TickFailures _failures = new();
    private readonly List<ScenarioConnection> _connections = [];
    private readonly List<MapInstance> _instances = [];
    private uint _nextOwner = 1;

    /// <param name="configuration">The game settings; the defaults when omitted (waypoint locomotion, as the shipped appsettings).</param>
    /// <param name="seed">Seeds every combat roll.</param>
    public ScenarioWorld(GameConfiguration? configuration = null, int seed = 0)
    {
        configuration ??= new GameConfiguration();
        Clock = new FakeTimeProvider(s_start);

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton(new CombatConfig());
        services.AddSingleton<IScriptManager>(new ScriptManager(NullLoggerFactory.Instance));
        services.AddSingleton<ICombatRandom>(new CombatRandom(new Random(seed)));
        services.AddSingleton(new PvpToggle(Options.Create(configuration), Clock));
        _services = services.BuildServiceProvider();

        _registry = new InstanceRegistry(NullLoggerFactory.Instance, new ScenarioMapManager(_templates), _factory);
        _world = new ScenarioWorldHost(configuration, data: null, _templates, _registry);
        _ticker = new InstanceTicker(_failures, _meter, Clock);
    }

    /// <summary>The world's clock: every instance, character and timer here reads it, and <see cref="Tick" /> advances it.</summary>
    public FakeTimeProvider Clock { get; }

    /// <summary>Every player's connection, in the order they joined.</summary>
    public IReadOnlyList<ScenarioConnection> Connections => _connections;

    /// <summary>How many instances the registry holds.</summary>
    public int Instances => _instances.Count;

    /// <summary>
    /// One world tick: every instance, through the registry's live list as <c>World.Update</c> takes it, then every
    /// connection's outbox, as <c>WorldServer</c> flushes them after the world update; then the clock moves on a tick.
    /// The ticker contains an instance's throw, as it does in production; here it ends the scenario instead, since a
    /// tick cut short would be measured as a cheap one.
    /// </summary>
    public void Tick()
    {
        _ticker.Tick(_registry.ActiveInstances, Dt);
        if (_failures.First is { } failure)
            throw new InvalidOperationException("An instance threw during a scenario tick", failure);

        for (int i = 0; i < _connections.Count; i++)
            _connections[i].FlushOutbox();

        Clock.Advance(Dt);
    }

    /// <summary>
    /// Builds an instance of the town's layout over the shared town navmesh and publishes it through the registry, as a
    /// finished build is published on the tick. A town is requested as one more town of its map (every existing one
    /// counted full), any other map type as a new owner's instance, so each call adds one.
    /// </summary>
    public MapInstance AddInstance(MapType mapType = MapType.Town, ushort templateId = 1)
    {
        var id = new MapTemplateId(templateId);
        if (!_templates.Exists(t => t.Id == id))
            _templates.Add(new MapTemplate { Id = id, MapType = mapType });

        uint? owner = mapType == MapType.Town ? null : _nextOwner++;

        var navigator = new MapNavigator(NullLoggerFactory.Instance);
        navigator.LoadFromNavMesh(TownNavmesh.Shared);
        var instance = new MapInstance(NullLoggerFactory.Instance, _services, _world, id, owner, TownNavmesh.Layout(),
            navigator, seed: 0, mapType: mapType);

        // maxPlayers 0 counts every town of the map full, so the registry starts a build rather than handing back one.
        Task<IMapInstance> request = owner is { } character
            ? _registry.GetOrCreateNormalInstanceAsync(character, id)
            : _registry.GetOrCreateTownInstanceAsync(id, maxPlayers: 0);
        _factory.Complete(instance);
        _registry.PublishFinished();

        if (!request.IsCompletedSuccessfully || !ReferenceEquals(request.Result, instance))
            throw new InvalidOperationException("The registry did not publish the scenario's instance");

        _instances.Add(instance);
        return instance;
    }

    /// <summary>A level 1 warrior on the world's clock, at full health, with no abilities.</summary>
    public CharacterEntity NewCharacter(uint id, int health = 100_000_000)
    {
        var row = new Character
        {
            Id = new CharacterId(id),
            AccountId = new AccountId(1),
            Name = $"Scenario{id}",
            Class = CharacterClass.Warrior,
            CreationDate = Clock.GetUtcNow().UtcDateTime,
            Health = health,
        };
        var character = new CharacterEntity(NullLoggerFactory.Instance, row, new RegenConfiguration(), Clock) { Data = row };
        character.CurrentHealth = character.Health;
        character.Spells.Load(Array.Empty<IAbility>());
        return character;
    }

    /// <summary>Puts the character in the instance at the position, behind a new <see cref="ScenarioConnection" />.</summary>
    public ScenarioConnection Join(MapInstance instance, CharacterEntity character, Vector3 position,
        Action<ScenarioConnection>? onUpdateMap = null)
    {
        character.InstanceId = instance.InstanceId;
        character.Position = position;

        var connection = new ScenarioConnection(character, onUpdateMap);
        instance.AddCharacter(connection);
        _connections.Add(connection);
        return connection;
    }

    public void Dispose()
    {
        foreach (MapInstance instance in _instances)
            instance.Dispose();

        _services.Dispose();
        _meter.Dispose();
    }

    /// <summary>The ticker's log: it logs only an instance's throw, and the first one is kept for <see cref="Tick" /> to raise.</summary>
    private sealed class TickFailures : ILogger
    {
        public Exception? First { get; private set; }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                First ??= exception ?? new InvalidOperationException(formatter(state, exception));
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }

    /// <summary>Only the map templates the scenario has built instances of: the registry reads them as a build starts.</summary>
    private sealed class ScenarioMapManager(IReadOnlyList<MapTemplate> templates) : IAvalonMapManager
    {
        public IReadOnlyList<MapTemplate> Templates { get; } = templates;

        public Task LoadAsync() => throw new NotSupportedException();
    }

    /// <summary>
    /// The registry's builds, each finished by the scenario with the instance it built itself. Build time only: the
    /// registry calls it as a build starts, never on a tick.
    /// </summary>
    private sealed class ScenarioInstanceFactory : IChunkLayoutInstanceFactory
    {
        private TaskCompletionSource<MapInstance>? _build;

        public Task<MapInstance> BuildAsync(MapTemplate template, uint? ownerCharacterId, CancellationToken ct,
            PartyId? ownerPartyId = null)
        {
            if (_build is not null)
                throw new InvalidOperationException("A scenario build is already in flight");

            _build = new TaskCompletionSource<MapInstance>();
            return _build.Task;
        }

        /// <summary>Finishes the build in flight; its continuation queues it for publication before this returns.</summary>
        public void Complete(MapInstance instance)
        {
            TaskCompletionSource<MapInstance> build =
                _build ?? throw new InvalidOperationException("The registry started no build");
            _build = null;
            build.SetResult(instance);
        }
    }
}
