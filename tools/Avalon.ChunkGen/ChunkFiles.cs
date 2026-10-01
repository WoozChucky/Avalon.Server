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
}
