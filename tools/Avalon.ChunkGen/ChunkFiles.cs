namespace Avalon.ChunkGen;

public static class ChunkFiles
{
    /// <summary>Each piece's .obj and .json text; throws (InvalidOperationException) on a piece that fails its own checks.</summary>
    public static IReadOnlyList<(string Name, string Obj, string Json)> For(IEnumerable<ChunkPiece> pieces) =>
        pieces.Select(p =>
        {
            p.Validate();
            return (p.Name, ObjWriter.Write(p), ChunkJsonWriter.Write(p.ToMeta()));
        }).ToList();

    /// <summary>The town squares' .obj and .json text; throws (InvalidOperationException) on a square that fails its own checks.</summary>
    public static IReadOnlyList<(string Name, string Obj, string Json)> For(IEnumerable<TownSquare> squares) =>
        squares.Select(s =>
        {
            s.Validate();
            return (s.Name, ObjWriter.Write(s), ChunkJsonWriter.Write(s.ToMeta()));
        }).ToList();

    /// <summary>
    /// Every file a run writes (ChunkGenCli): the single pieces, then each set piece's four members; throws
    /// (InvalidOperationException) on a piece or set piece that fails its own checks.
    /// </summary>
    public static IReadOnlyList<(string Name, string Obj, string Json)> For(
        IEnumerable<ChunkPiece> singles, IEnumerable<ChunkGroupPiece> groups) =>
        For([.. singles, .. groups.SelectMany(g => g.Members().Select(m => m.Piece))]);
}
