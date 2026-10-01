using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>
/// A set piece (forest content pass): 1x1 chunks on the cells of a rectangle (the forest's are 2x2), placed by the
/// procedural generator all at once, rotated as a whole, joined to the layout only through exits on its outer edges.
/// Seeded from Maps/chunk-groups.json by ChunkCatalogSeeder on every start; its members are in no pool on their own.
/// </summary>
public class ChunkGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ChunkPoolId ChunkPoolId { get; set; } = default!;
    public List<ChunkGroupMember> Members { get; set; } = [];
}

/// <summary>One member chunk of a <see cref="ChunkGroup" /> and its cell in the group's own frame (0,0 = south-west).</summary>
public class ChunkGroupMember
{
    public int ChunkGroupId { get; set; }
    public ChunkTemplateId ChunkTemplateId { get; set; } = default!;
    public byte CellX { get; set; }
    public byte CellZ { get; set; }
}
