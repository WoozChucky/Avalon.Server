using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>
/// A route a creature walks, looping: an ordered list of <see cref="CreaturePathPoint"/>s. An
/// authored spawn names one through <see cref="MapCreatureSpawn.PathId"/>, and several spawns can
/// share one — two guards walking the same wall.
/// </summary>
/// <remarks>
/// Only authored spawns can have a path. Procedural spawns come from a spawn table rolled against
/// chunk slots, with no fixed position to hang a route on.
/// </remarks>
public class CreaturePath : IDbEntity<CreaturePathId>
{
    public CreaturePathId Id { get; set; } = default!;

    /// <summary>A label for whoever authors paths. Nothing reads it at runtime.</summary>
    public string Name { get; set; } = string.Empty;

    public List<CreaturePathPoint> Points { get; set; } = [];
}
