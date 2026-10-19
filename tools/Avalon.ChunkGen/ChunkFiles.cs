namespace Avalon.ChunkGen;

/// <summary>
/// The text of every file a run writes: each chunk's .obj and .json, and its decor file (Maps/Decor/&lt;name&gt;.json),
/// a town square's or a dressed forest piece's.
/// </summary>
public static class ChunkFiles
{
    /// <summary>
    /// Each piece's .obj and .json text, undressed (no decor file); throws (InvalidOperationException) on a piece that
    /// fails its own checks.
    /// </summary>
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
    /// Every file a forest run writes (ChunkGenCli): the single pieces, then each set piece's four members, each with
    /// its decor (ForestDresser), a set piece's dressed in its frame and split into its members. Throws
    /// (InvalidOperationException) on a piece or set piece that fails its own checks or whose decor breaks a forest
    /// decor rule (ForestDecorRules).
    /// </summary>
    public static IReadOnlyList<(string Name, string Obj, string Json, string? Decor)> For(
        IEnumerable<ChunkPiece> singles, IEnumerable<ChunkGroupPiece> groups)
    {
        IReadOnlyDictionary<string, ModelBounds> kit = KitBounds.Load();
        List<(string Name, string Obj, string Json, string? Decor)> files = [];
        foreach (ChunkPiece piece in singles)
        {
            piece.Validate();
            IReadOnlyList<DecorItem> decor = ForestDresser.Dress(piece);
            ForestDecorRules.Check(piece, decor, kit);
            files.Add((piece.Name, ObjWriter.Write(piece), ChunkJsonWriter.Write(piece.ToMeta()),
                DecorJsonWriter.Write(piece.Name, decor, DecorJsonWriter.ForestKit)));
        }

        foreach (ChunkGroupPiece group in groups)
        {
            List<ChunkPiece> members = [.. group.Members().Select(m => m.Piece)];
            members.ForEach(m => m.Validate());
            IReadOnlyList<DecorItem> decor = ForestDresser.Dress(group);
            ForestDecorRules.Check(group, decor, kit);
            IReadOnlyDictionary<string, IReadOnlyList<DecorItem>> split = ForestDresser.Members(group, decor);
            foreach (ChunkPiece member in members)
            {
                files.Add((member.Name, ObjWriter.Write(member), ChunkJsonWriter.Write(member.ToMeta()),
                    DecorJsonWriter.Write(member.Name, split[member.Name], DecorJsonWriter.ForestKit)));
            }
        }
        return files;
    }

    /// <summary>
    /// Each scenery piece's terrain (Maps/Scenery/&lt;name&gt;.obj, SceneryObjWriter) and decor (Maps/Decor/&lt;name&gt;.json,
    /// in the piece's kit), unchecked: the scenery rules are not applied here.
    /// </summary>
    public static IReadOnlyList<(string Name, string Obj, string Decor)> Scenery(IEnumerable<SceneryPiece> pieces) =>
        [.. pieces.Select(p => (p.Name, SceneryObjWriter.Write(p), DecorJsonWriter.Write(p.Name, p.Decor, p.Kit)))];
}
