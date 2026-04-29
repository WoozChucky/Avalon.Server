namespace Avalon.Api.Contract;

public sealed class LayoutPreviewDto
{
    public int Seed { get; set; }
    public ushort MapTemplateId { get; set; }
    public float CellSize { get; set; }
    public float EntrySpawnX { get; set; }
    public float EntrySpawnY { get; set; }
    public float EntrySpawnZ { get; set; }
    public List<ChunkPreviewDto> Chunks { get; set; } = new();
    public List<PortalPreviewDto> Portals { get; set; } = new();
    public ChunkPreviewDto? BossChunk { get; set; }
    public ChunkPreviewDto? EntryChunk { get; set; }
}

public sealed class ChunkPreviewDto
{
    public int TemplateId { get; set; }
    public string TemplateName { get; set; } = "";

    /// <summary>
    /// Geometry path relative to the chunk asset root (e.g. <c>Chunks/forest_path_01.obj</c>).
    /// SPA fetches the bytes from <c>/map-template/chunk-asset/{filename}</c>.
    /// </summary>
    public string GeometryFile { get; set; } = "";
    public short GridX { get; set; }
    public short GridZ { get; set; }
    public byte Rotation { get; set; }
    public float WorldX { get; set; }
    public float WorldY { get; set; }
    public float WorldZ { get; set; }
}

public sealed class PortalPreviewDto
{
    public string Role { get; set; } = "";
    public ushort TargetMapId { get; set; }
    public float WorldX { get; set; }
    public float WorldY { get; set; }
    public float WorldZ { get; set; }
    public float Radius { get; set; }
}
