namespace Avalon.Domain.World;

/// <summary>
/// A spot a procedural map may place a doodad at, in chunk-local metres. <c>doodad</c> slots take walk-through doodads;
/// <c>doodad_large</c> slots stand further from paths, blockers and spawns and may hold a solid one.
/// </summary>
public class ChunkDoodadSlot
{
    public string Tag { get; set; } = string.Empty;   // "doodad" | "doodad_large"
    public float LocalX { get; set; }
    public float LocalY { get; set; }
    public float LocalZ { get; set; }

    /// <summary>The yaw in degrees a doodad placed here faces, matching <c>Orientation.y</c>: a quarter turn, 0, 90, 180 or 270.</summary>
    public int Yaw { get; set; }
}
