using Avalon.Common.ValueObjects;
using Avalon.Domain.World;

namespace Avalon.World.ChunkLayouts;

/// <summary>
/// A set piece as the generator sees it (forest content pass): member chunks on the cells of a rectangle, placed at
/// once and turned as a whole. Only exits on outer edges join it to the layout.
/// </summary>
public sealed record ChunkGroupDefinition(string Name, IReadOnlyList<ChunkGroupCell> Cells)
{
    // Worked out once from the cells: the generator asks for these for every free cell it tries. A copy made with a
    // `with` expression that replaces Cells would keep the old values, so build a new definition instead.
    public int SizeX { get; } = Cells.Max(c => c.CellX) + 1;
    public int SizeZ { get; } = Cells.Max(c => c.CellZ) + 1;

    public bool IsBoss { get; } = Cells.Any(c => c.Template.SpawnSlots.Any(s => s.Tag.Equals("boss", StringComparison.OrdinalIgnoreCase)));

    public bool HasForward { get; } = Cells.Any(c => c.Template.PortalSlots.Any(p => p.Role == PortalRole.Forward));

    /// <summary>Exit slots on sides that face no other member: the piece's ways in and out. The same under any rotation.</summary>
    public int OuterExitCount { get; } = CountOuterExits(Cells);

    private static int CountOuterExits(IReadOnlyList<ChunkGroupCell> members)
    {
        var cells = members.Select(c => (c.CellX, c.CellZ)).ToHashSet();
        int count = 0;
        foreach (ChunkGroupCell cell in members)
        {
            foreach (ExitSide side in Enum.GetValues<ExitSide>())
            {
                (int dx, int dz) = ExitMask.GridDir(side);
                if (cells.Contains((cell.CellX + dx, cell.CellZ + dz))) continue;
                for (int slot = 0; slot < 3; slot++)
                    if (ExitMask.Has(cell.Template.Exits, side, (ExitSlot)slot)) count++;
            }
        }

        return count;
    }

    /// <summary>The definition of a seeded group, or null when one of its member templates is unknown.</summary>
    public static ChunkGroupDefinition? From(ChunkGroup group, IReadOnlyDictionary<ChunkTemplateId, ChunkTemplate> templates)
    {
        var cells = new List<ChunkGroupCell>(group.Members.Count);
        foreach (ChunkGroupMember member in group.Members)
        {
            if (!templates.TryGetValue(member.ChunkTemplateId, out ChunkTemplate? template))
                return null;
            cells.Add(new ChunkGroupCell(template, member.CellX, member.CellZ));
        }

        return cells.Count == 0 ? null : new ChunkGroupDefinition(group.Name, cells);
    }
}
