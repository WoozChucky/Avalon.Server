using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>One member chunk of a <see cref="ChunkGroup" /> and its cell in the group's own frame (0,0 = south-west).</summary>
public class ChunkGroupMember
{
    public int ChunkGroupId { get; set; }
    public ChunkTemplateId ChunkTemplateId { get; set; } = default!;
    public byte CellX { get; set; }
    public byte CellZ { get; set; }
}
