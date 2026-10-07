namespace Avalon.ChunkGen;

/// <summary>
/// A 2x2 set piece authored in its own 60 x 60 m frame and split into four 1x1 members named
/// &lt;name&gt;_sw/_se/_nw/_ne. No blocker may cross or touch a cell edge, so the inner edges are open ground; exits
/// exist only where listed, on outer edges.
/// </summary>
public sealed record ChunkGroupPiece(
    string Name, IReadOnlyList<(int CellX, int CellZ, Side Side)> OuterExits, IReadOnlyList<Blocker> Blockers,
    IReadOnlyList<Slot> Slots, IReadOnlyList<string> Tags)
{
    private static readonly (int X, int Z, string Suffix)[] s_cells = [(0, 0, "sw"), (1, 0, "se"), (0, 1, "nw"), (1, 1, "ne")];

    public static string MemberName(string group, int cellX, int cellZ) =>
        $"{group}_{s_cells.Single(c => c.X == cellX && c.Z == cellZ).Suffix}";

    public IEnumerable<(ChunkPiece Piece, int CellX, int CellZ)> Members()
    {
        const float Cell = ChunkPiece.CellSize;
        foreach (Blocker blocker in Blockers)
        {
            (float minX, float maxX, float minZ, float maxZ) = blocker.Bounds;
            // A bound that is not a number lies in no cell, so the split below would drop the blocker without a word.
            if (!float.IsFinite(minX) || !float.IsFinite(maxX) || !float.IsFinite(minZ) || !float.IsFinite(maxZ))
                throw new InvalidOperationException($"{Name}: blocker {blocker} has bounds that are not finite");
            if (CellOf(minX, Cell) != CellOf(maxX, Cell) || CellOf(minZ, Cell) != CellOf(maxZ, Cell)
                || minX % Cell == 0 || maxX % Cell == 0 || minZ % Cell == 0 || maxZ % Cell == 0)
            {
                throw new InvalidOperationException($"{Name}: blocker {blocker} crosses or touches a cell edge");
            }

            if (!InFrame(CellOf(minX, Cell)) || !InFrame(CellOf(minZ, Cell)))
                throw new InvalidOperationException($"{Name}: blocker {blocker} lies outside the 60 x 60 m frame");
        }

        // A slot on a cell edge, or outside the frame, belongs to no member: refuse it rather than drop it.
        foreach (Slot slot in Slots)
        {
            if (!(slot.X > 0f && slot.X < 2 * Cell && slot.Z > 0f && slot.Z < 2 * Cell) || slot.X == Cell || slot.Z == Cell)
                throw new InvalidOperationException($"{Name}: slot {slot} lies on a cell edge or outside the 60 x 60 m frame");
        }

        foreach ((int x, int z, string suffix) in s_cells)
        {
            float ox = x * Cell, oz = z * Cell;
            bool Inside(float px, float pz) => px > ox && px < ox + Cell && pz > oz && pz < oz + Cell;

            yield return (new ChunkPiece(
                $"{Name}_{suffix}",
                OuterExits.Where(e => e.CellX == x && e.CellZ == z).Select(e => e.Side).ToList(),
                Blockers.Where(b => Inside((b.Bounds.MinX + b.Bounds.MaxX) / 2f, (b.Bounds.MinZ + b.Bounds.MaxZ) / 2f))
                    .Select(b => b.Moved(-ox, -oz)).ToList(),
                Slots.Where(s => Inside(s.X, s.Z)).Select(s => s with { X = s.X - ox, Z = s.Z - oz }).ToList(),
                [.. Tags, "group"]), x, z);
        }
    }

    private static int CellOf(float v, float cell) => (int)MathF.Floor(v / cell);

    private static bool InFrame(int cell) => cell is 0 or 1;
}
