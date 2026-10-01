namespace Avalon.Domain.World;

/// <summary>
/// One depth band of a procedural map (forest content pass). A piece whose depth (grid steps from the entry along the
/// layout's stitched connections) is MinDepth..MaxDepth spawns its creatures at MinLevel..MaxLevel; MaxDepth null runs
/// on without end. The highest band (largest MinDepth) also sets every set piece's levels, and its MaxLevel is the
/// boss's. A map with no bands rolls each creature from its template's own range. Owned by ProceduralMapConfig and
/// written by ChunkCatalogSeeder from Maps/ProceduralMaps/&lt;mapId&gt;.json.
/// </summary>
public class ProceduralDepthBand
{
    public int MinDepth { get; set; }
    public int? MaxDepth { get; set; }
    public ushort MinLevel { get; set; }
    public ushort MaxLevel { get; set; }
}
