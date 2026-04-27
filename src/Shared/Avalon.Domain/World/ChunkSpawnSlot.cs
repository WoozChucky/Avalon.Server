namespace Avalon.Domain.World;

public class ChunkSpawnSlot
{
    public string Tag { get; set; } = string.Empty;   // "entry" | "pack" | "rare" | "boss" | "empty"
    public float LocalX { get; set; }
    public float LocalY { get; set; }
    public float LocalZ { get; set; }
}
