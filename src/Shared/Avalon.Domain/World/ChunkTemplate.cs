using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

public class ChunkTemplate : IDbEntity<ChunkTemplateId>
{
    public ChunkTemplateId Id { get; set; } = default!;
    public string Name { get; set; } = string.Empty;
    public string AssetKey { get; set; } = string.Empty;
    public string GeometryFile { get; set; } = string.Empty;
    public byte CellFootprintX { get; set; } = 1;
    public byte CellFootprintZ { get; set; } = 1;
    public float CellSize { get; set; } = 30.0f;

    /// <summary>ExitSlots bitmask, 12 bits used (3 slots × 4 sides). Declared in unrotated local frame.</summary>
    public ushort Exits { get; set; }

    public List<ChunkSpawnSlot> SpawnSlots { get; set; } = [];
    public List<ChunkPortalSlot> PortalSlots { get; set; } = [];
    public string[] Tags { get; set; } = [];
}
