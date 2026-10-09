using System.Diagnostics.Metrics;
using Avalon.Combat;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.World.Characters;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Inventory;
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

    // The seeded formula's defaults: the sheet flusher reads its caps, and a scenario has no reference data.
    private static readonly CombatFormula s_formula = new();

    // How far a walker must have moved between MarkProgress and a check: well above float noise, well below the
    // 20 m it walks in five windows.
    private const float MinimumWalk = 0.5f;

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

    // Every connection's progress at the last MarkProgress, by its index in _connections.
    private int[] _markedSent = [];
    private uint[] _markedInputSeq = [];
    private Vector3[] _markedPosition = [];

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

    /// <summary>The world the instances belong to, as a packet handler is given it: its registry is the real one here.</summary>
    public IWorld Host => _world;

    /// <summary>Every player's connection, in the order they joined.</summary>
    public IReadOnlyList<ScenarioConnection> Connections => _connections;

    /// <summary>How many instances the registry holds.</summary>
    public int Instances => _instances.Count;

    /// <summary>Every instance the scenario built, in the order it built them.</summary>
    public IReadOnlyList<MapInstance> Maps => _instances;

    /// <summary>
    /// One world tick, in <c>World.Update</c>'s order for the registry: the builds finished since the last tick
    /// published, then every instance through the registry's tick snapshot (<c>TickInstances</c>); then, as
    /// <c>WorldServer</c> runs them after the world update, the inventory, sheet and ability-amount flushers and every
    /// connection's outbox; then the clock moves on a tick.
    /// The ticker contains an instance's throw, as it does in production; here it ends the scenario instead, since a
    /// tick cut short would be measured as a cheap one.
    /// </summary>
    /// <remarks>
    /// Left out: <c>World.Update</c>'s last step, <c>InstanceRegistry.ProcessExpiredInstances</c>. Its walk of the
    /// registry's dictionary allocates an enumerator (72 B) per tick in an unoptimized build and, measured, nothing in
    /// an optimized one, where the JIT does away with the enumerator's heap allocation. So it would add nothing to the
    /// committed (Release) figure and 4,320 B per window to a Debug run, failing <c>town-idle</c>'s gate in every local
    /// Debug <c>dotnet test</c>.
    /// </remarks>
    public void Tick()
    {
        _registry.PublishFinished();

        _ticker.Tick(_registry.TickInstances(), Dt);
        if (_failures.First is { } failure)
            throw new InvalidOperationException("An instance threw during a scenario tick", failure);

        // The flushers that need no service, in WorldServer's order after the world update (#875). Nothing a scenario
        // does changes an inventory, the stats or the abilities, so they send nothing, and must allocate nothing.
        for (int i = 0; i < _connections.Count; i++)
            InventoryUpdateFlusher.Flush(_connections[i]);
        for (int i = 0; i < _connections.Count; i++)
            CharacterSheetFlusher.Flush(_connections[i], s_formula);
        for (int i = 0; i < _connections.Count; i++)
            AbilityAmountsFlusher.Flush(_connections[i]);

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

    /// <summary>
    /// Records every connection's packets sent, last input and position, for <see cref="RequireEverySent" /> and
    /// <see cref="RequireEveryWalked" /> to compare with later. Outside any measured tick: it allocates its arrays.
    /// </summary>
    public void MarkProgress()
    {
        int count = _connections.Count;
        _markedSent = new int[count];
        _markedInputSeq = new uint[count];
        _markedPosition = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            ScenarioConnection connection = _connections[i];
            _markedSent[i] = connection.Sent;
            _markedInputSeq[i] = connection.LastInputSeq;
            _markedPosition[i] = connection.Character!.Position;
        }
    }

    /// <summary>
    /// Throws unless every player is still in the instances and has been sent something since joining (the others, as
    /// it entered). A check that holds for a world where nothing changes, so nothing is sent from tick to tick.
    /// </summary>
    public void RequireEveryPresent(string scenario)
    {
        int present = 0;
        foreach (MapInstance instance in _instances)
            present += instance.PlayerCount;

        if (present != _connections.Count)
        {
            throw new InvalidOperationException(
                $"{scenario}: {present} of {_connections.Count} players are still in the instances. A scenario that " +
                "loses players measures cheaper than it is and would pass the allocation gate as an improvement.");
        }

        for (int i = 0; i < _connections.Count; i++)
        {
            if (_connections[i].Sent == 0)
                throw new InvalidOperationException($"{scenario}: player {i} was never sent a packet; its send path is not reached.");
        }
    }

    /// <summary>Throws unless every connection has been sent a packet since <see cref="MarkProgress" />.</summary>
    public void RequireEverySent(string scenario)
    {
        RequireMarked(scenario);
        for (int i = 0; i < _connections.Count; i++)
        {
            if (_connections[i].Sent <= _markedSent[i])
            {
                throw new InvalidOperationException(
                    $"{scenario}: player {i} was sent no packet during the measured windows. A scenario that stops " +
                    "sending measures cheaper than it is and would pass the allocation gate as an improvement.");
            }
        }
    }

    /// <summary>
    /// Throws unless every connection's input sequence has advanced by at least <paramref name="inputs" /> and its
    /// character has moved since <see cref="MarkProgress" />.
    /// </summary>
    public void RequireEveryWalked(string scenario, uint inputs)
    {
        RequireMarked(scenario);
        for (int i = 0; i < _connections.Count; i++)
        {
            ScenarioConnection connection = _connections[i];
            uint advanced = connection.LastInputSeq - _markedInputSeq[i];
            if (advanced < inputs)
            {
                throw new InvalidOperationException(
                    $"{scenario}: player {i}'s input advanced by {advanced} during the measured windows, " +
                    $"expected at least {inputs}. Its input is no longer handled, so the scenario measures less " +
                    "than it claims and would pass the allocation gate as an improvement.");
            }

            Vector3 position = connection.Character!.Position;
            if (Vector3.Distance(position, _markedPosition[i]) < MinimumWalk)
            {
                throw new InvalidOperationException(
                    $"{scenario}: player {i} did not move during the measured windows (still at " +
                    $"{position.x:F2}, {position.z:F2}). A walker stuck in a wall measures less than walking does " +
                    "and would pass the allocation gate as an improvement.");
            }
        }
    }

    private void RequireMarked(string scenario)
    {
        if (_markedSent.Length != _connections.Count)
            throw new InvalidOperationException($"{scenario}: the scenario's progress was not marked before its windows");
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
