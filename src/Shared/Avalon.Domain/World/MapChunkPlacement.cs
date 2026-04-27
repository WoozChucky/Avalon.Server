using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

public class MapChunkPlacement : IDbEntity<MapChunkPlacementId>
{
    public MapChunkPlacementId Id { get; set; } = default!;

    public MapTemplateId MapTemplateId { get; set; } = default!;
    public ChunkTemplateId ChunkTemplateId { get; set; } = default!;

    public short GridX { get; set; }
    public short GridZ { get; set; }
    public byte Rotation { get; set; }
    public bool IsEntry { get; set; }

    public float EntryLocalX { get; set; }
    public float EntryLocalY { get; set; }
    public float EntryLocalZ { get; set; }

    public ushort? BackPortalTargetMapId { get; set; }
    public ushort? ForwardPortalTargetMapId { get; set; }
}
