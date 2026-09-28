using Avalon.Domain.World;
using Avalon.World;
using Avalon.World.Configuration;
using Avalon.World.Public;
using Avalon.World.Public.Instances;

namespace Avalon.Server.World.UnitTests;

/// <summary>
/// Only what building an instance and ticking it read. Hand-written rather than substituted, because a
/// substitute allocates on every call it records, so the tick paths it serves can be pinned at zero bytes.
/// </summary>
internal sealed class QuietWorld(StaticData data) : IWorld
{
    public StaticData Data { get; } = data;
    public GameConfiguration Configuration { get; } = new();
    public IReadOnlyList<MapTemplate> MapTemplates { get; } = [];

    public Avalon.Domain.Auth.WorldId Id => throw new NotSupportedException();
    public string MinVersion => throw new NotSupportedException();
    public string CurrentVersion => throw new NotSupportedException();
    public GameTime Time => throw new NotSupportedException();
    public IInstanceRegistry InstanceRegistry => throw new NotSupportedException();
    public void SpawnInInstance(IWorldConnection connection, IMapInstance instance) => throw new NotSupportedException();
    public void TransferPlayer(IWorldConnection connection, IMapInstance targetInstance) => throw new NotSupportedException();
    public Task DeSpawnPlayerAsync(IWorldConnection connection) => throw new NotSupportedException();
    public Task LoadAsync(CancellationToken token) => throw new NotSupportedException();
    public void Update(TimeSpan deltaTime) => throw new NotSupportedException();
}
