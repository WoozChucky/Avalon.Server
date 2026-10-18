namespace Avalon.ChunkGen;

/// <summary>
/// The text of every file a run writes: each chunk's .obj and .json, and for a town square its decor file
/// (Maps/Decor/&lt;name&gt;.json, null for a forest piece, which has none).
/// </summary>
public static class ChunkFiles
{
    /// <summary>Each piece's .obj and .json text; throws (InvalidOperationException) on a piece that fails its own checks.</summary>
    public static IReadOnlyList<(string Name, string Obj, string Json, string? Decor)> For(IEnumerable<ChunkPiece> pieces) =>
        pieces.Select(p =>
        {
            p.Validate();
            return (p.Name, ObjWriter.Write(p), ChunkJsonWriter.Write(p.ToMeta()), (string?)null);
        }).ToList();

    /// <summary>
    /// The town squares' .obj, .json and decor text (an empty item list for a square with no decor); throws
    /// (InvalidOperationException) on a square that fails its own checks.
    /// </summary>
    public static IReadOnlyList<(string Name, string Obj, string Json, string? Decor)> For(IEnumerable<TownSquare> squares) =>
        squares.Select(s =>
        {
            s.Validate();
            return (s.Name, ObjWriter.Write(s), ChunkJsonWriter.Write(s.ToMeta()),
                (string?)DecorJsonWriter.Write(s.Name, s.Decor, DecorJsonWriter.TownKit));
        }).ToList();

    /// <summary>
    /// Every file a run writes (ChunkGenCli): the single pieces, then each set piece's four members; throws
    /// (InvalidOperationException) on a piece or set piece that fails its own checks.
    /// </summary>
    public static IReadOnlyList<(string Name, string Obj, string Json, string? Decor)> For(
        IEnumerable<ChunkPiece> singles, IEnumerable<ChunkGroupPiece> groups) =>
        For([.. singles, .. groups.SelectMany(g => g.Members().Select(m => m.Piece))]);
}
