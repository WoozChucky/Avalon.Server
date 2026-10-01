namespace Avalon.ChunkGen;

public static class ChunkFiles
{
    public static IReadOnlyList<(string Name, string Obj, string Json)> For(IEnumerable<ChunkPiece> pieces) =>
        pieces.Select(p => (p.Name, ObjWriter.Write(p), ChunkJsonWriter.Write(p.ToMeta()))).ToList();
}
