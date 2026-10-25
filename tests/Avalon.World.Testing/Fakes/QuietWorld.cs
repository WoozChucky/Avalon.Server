using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Configuration;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Public.Instances;

namespace Avalon.World.Testing.Fakes;

/// <summary>
/// Only what building an instance and ticking it read, and, given one, the instance a handler finds by id.
/// Hand-written rather than substituted, because a substitute allocates on every call it records, so the tick
/// paths it serves can be pinned at zero bytes.
/// </summary>
public sealed class QuietWorld(StaticData data, IMapInstance? instance = null) : IWorld
{
    private readonly OneInstance? _instances = instance is null ? null : new OneInstance(instance);

    public StaticData Data { get; } = data;
    public GameConfiguration Configuration { get; } = new();
    public IReadOnlyList<MapTemplate> MapTemplates { get; } = [];
    public IInstanceRegistry InstanceRegistry => _instances ?? throw new NotSupportedException();

    public Avalon.Domain.Auth.WorldId Id => throw new NotSupportedException();
    public string MinVersion => throw new NotSupportedException();
    public string CurrentVersion => throw new NotSupportedException();
    public GameTime Time => throw new NotSupportedException();
    public IPartyInstanceRegistry PartyInstances => throw new NotSupportedException();
    public IInstanceChoices InstanceChoices => throw new NotSupportedException();
    public void SpawnInInstance(IWorldConnection connection, IMapInstance instance) => throw new NotSupportedException();
    public void TransferPlayer(IWorldConnection connection, IMapInstance targetInstance) => throw new NotSupportedException();
    public Task DeSpawnPlayerAsync(IWorldConnection connection) => throw new NotSupportedException();
    public Task<bool> LeaveWorldAsync(IWorldConnection connection) => throw new NotSupportedException();
    public Task LoadAsync(CancellationToken token) => throw new NotSupportedException();
    public void Update(TimeSpan deltaTime) => throw new NotSupportedException();

    /// <summary>Finds its one instance by id, and nothing else.</summary>
    private sealed class OneInstance(IMapInstance instance) : IInstanceRegistry
    {
        public IMapInstance? GetInstanceById(Guid instanceId) => instance.InstanceId == instanceId ? instance : null;

        public IReadOnlyCollection<IMapInstance> ActiveInstances => throw new NotSupportedException();
        public Task<IMapInstance> GetOrCreateTownInstanceAsync(MapTemplateId templateId, ushort maxPlayers) =>
            throw new NotSupportedException();
        public Task<IMapInstance> GetOrCreateNormalInstanceAsync(uint characterId, MapTemplateId templateId) =>
            throw new NotSupportedException();
        public void RemoveInstance(Guid instanceId) => throw new NotSupportedException();
        public void ProcessExpiredInstances(TimeSpan abandonedInstanceLifetime) => throw new NotSupportedException();
    }
}
