using Avalon.Domain.World;
using Avalon.World.Configuration;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Public.Instances;

namespace Avalon.World.Testing.Scenarios;

/// <summary>
/// The world a scenario's instances belong to: what building and ticking them read (the configuration, the reference
/// data, the map templates) and the real <see cref="Instances.InstanceRegistry" /> the scenario owns, which a packet
/// handler resolves a character's instance through. Hand-written rather than substituted, because a substitute
/// allocates on every call it records. Everything a scenario does not drive throws.
/// </summary>
internal sealed class ScenarioWorldHost(GameConfiguration configuration, StaticData? data,
    IReadOnlyList<MapTemplate> mapTemplates, InstanceRegistry registry) : IWorld
{
    /// <summary>
    /// The reference data, or null for a scenario that needs none: an instance reads it only for a kill, a shop or a
    /// cast's ability lookup, and treats its absence as no data.
    /// </summary>
    public StaticData Data { get; } = data!;

    public GameConfiguration Configuration { get; } = configuration;

    /// <summary>The templates of the maps the scenario built instances of, the same list its registry builds from.</summary>
    public IReadOnlyList<MapTemplate> MapTemplates { get; } = mapTemplates;

    public IInstanceRegistry InstanceRegistry { get; } = registry;

    public Avalon.Domain.Auth.WorldId Id => throw new NotSupportedException();
    public string MinVersion => throw new NotSupportedException();
    public string CurrentVersion => throw new NotSupportedException();
    public GameTime Time => throw new NotSupportedException();
    public IPartyInstanceRegistry PartyInstances => throw new NotSupportedException();
    public void SpawnInInstance(IWorldConnection connection, IMapInstance instance) => throw new NotSupportedException();
    public void TransferPlayer(IWorldConnection connection, IMapInstance targetInstance) => throw new NotSupportedException();
    public Task DeSpawnPlayerAsync(IWorldConnection connection) => throw new NotSupportedException();
    public Task<bool> LeaveWorldAsync(IWorldConnection connection) => throw new NotSupportedException();
    public Task LoadAsync(CancellationToken token) => throw new NotSupportedException();
    public void Update(TimeSpan deltaTime) => throw new NotSupportedException();
}
