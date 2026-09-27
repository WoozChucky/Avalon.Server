using System.Diagnostics.CodeAnalysis;
using Avalon.Domain.Auth;

namespace Avalon.Api.Worlds;

/// <summary>The worlds this api is configured for (Database:Worlds), with their status (#523).</summary>
public interface IWorldDatabases
{
    /// <summary>Every configured world, in id order.</summary>
    IReadOnlyList<ConfiguredWorld> All { get; }

    bool TryGet(WorldId world, [NotNullWhen(true)] out ConfiguredWorld? configured);

    /// <summary>
    /// The world is configured and its databases migrated at startup. The one test of "this api can
    /// serve that world"; false for a world it was not given.
    /// </summary>
    bool IsAvailable(WorldId world);
}

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

    /// <summary>Startup only: the world's migration failed, so it answers 503 until the next restart.</summary>
    public void MarkUnavailable(WorldId world) => _byId[world.Value].MarkUnavailable();
}
