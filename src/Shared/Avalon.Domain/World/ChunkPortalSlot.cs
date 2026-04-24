namespace Avalon.Domain.World;

public enum PortalRole : byte
{
    Back = 0,
    Forward = 1,
}

public class ChunkPortalSlot
{
    public PortalRole Role { get; set; }
    public float LocalX { get; set; }
    public float LocalY { get; set; }
    public float LocalZ { get; set; }
}
