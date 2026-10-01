using Avalon.Domain.World;

namespace Avalon.World.ChunkLayouts;

/// <summary>One member of a set piece and its cell in the piece's own frame (0,0 = south-west).</summary>
public sealed record ChunkGroupCell(ChunkTemplate Template, int CellX, int CellZ);
