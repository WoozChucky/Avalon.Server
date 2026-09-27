using System.Diagnostics.CodeAnalysis;
using Avalon.Domain.Auth;

namespace Avalon.Api.Worlds;

public sealed class WorldDatabases : IWorldDatabases
{
    private readonly Dictionary<ushort, ConfiguredWorld> _byId;

    public WorldDatabases(IEnumerable<ConfiguredWorld> worlds)
    {
        All = worlds.OrderBy(w => w.Id.Value).ToList();
        _byId = All.ToDictionary(w => w.Id.Value);
    }

    public IReadOnlyList<ConfiguredWorld> All { get; }

    public bool TryGet(WorldId world, [NotNullWhen(true)] out ConfiguredWorld? configured) =>
        _byId.TryGetValue(world.Value, out configured);

    public bool IsAvailable(WorldId world) =>
        TryGet(world, out ConfiguredWorld? configured) && configured.Status == WorldDatabaseStatus.Available;

    /// <summary>
    /// Startup only: the world's migration failed, so it answers 503 until the next restart. Only a
    /// configured world can fail its migration, so any other id is a programming error.
    /// </summary>
    public void MarkUnavailable(WorldId world)
    {
        if (!_byId.TryGetValue(world.Value, out ConfiguredWorld? configured))
            throw new InvalidOperationException($"World {world.Value} is not configured under {WorldDatabaseSettings.Section}.");

        configured.MarkUnavailable();
    }
}
