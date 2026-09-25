using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>
/// One point on a <see cref="CreaturePath"/>. Points are walked in <see cref="Sequence"/> order,
/// looping back to the first.
/// </summary>
/// <remarks>
/// Offsets in metres from the map's entry spawn point, exactly like <see cref="MapCreatureSpawn"/>
/// and for the same reason: a town's world coordinates fall out of its chunk placements, so an
/// absolute point would drift the first time the town's chunks moved. <see cref="OffsetY"/> is a
/// hint only — placement snaps each point to the navmesh.
/// </remarks>
public class CreaturePathPoint
{
    public CreaturePathId PathId { get; set; } = default!;

    /// <summary>Walk order within the path. Unique per path; gaps are allowed.</summary>
    public int Sequence { get; set; }

    public float OffsetX { get; set; }

    /// <summary>Vertical hint, used as the centre of the navmesh search box rather than as the final height.</summary>
    public float OffsetY { get; set; }

    public float OffsetZ { get; set; }

    /// <summary>How long the creature stands at this point before walking on, in milliseconds. 0 walks straight on.</summary>
    public int WaitMs { get; set; }
}
