using Avalon.Common.ValueObjects;

namespace Avalon.World.Public.Instances;

/// <summary>
///     Tracks all live map instances and handles Town auto-routing and Normal map re-entry.
/// </summary>
public interface IInstanceRegistry
{
    IReadOnlyCollection<IMapInstance> ActiveInstances { get; }

    /// <summary>
    ///     Returns the least-populated Town instance for <paramref name="templateId" /> that still has room.
    ///     Creates a new instance if all existing ones are at capacity or none exist.
    /// </summary>
    Task<IMapInstance> GetOrCreateTownInstanceAsync(MapTemplateId templateId, ushort maxPlayers);

    /// <summary>
    ///     Returns the existing Normal map instance for <paramref name="characterId" /> if it is still alive
    ///     (not expired), otherwise creates a fresh one. Keying by character (not account) ensures
    ///     two characters on the same account cannot share a procedural instance.
    /// </summary>
    Task<IMapInstance> GetOrCreateNormalInstanceAsync(uint characterId, MapTemplateId templateId);

    IMapInstance? GetInstanceById(Guid instanceId);
    void RemoveInstance(Guid instanceId);

    /// <summary>Frees all Normal map instances that have been empty longer than <paramref name="normalMapExpiry" />.</summary>
    void ProcessExpiredInstances(TimeSpan normalMapExpiry);
}
