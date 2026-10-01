namespace Avalon.ChunkGen;

/// <summary>The forest's generated pieces (forest content pass). Placeholder geometry until real art exists.</summary>
public static class ForestPieces
{
    public static IReadOnlyList<ChunkPiece> Singles() => [];

    public static IReadOnlyList<ChunkGroupPiece> Groups() => [];

    public static IReadOnlyList<ChunkPiece> All() => [.. Singles(), .. Groups().SelectMany(g => g.Members().Select(m => m.Piece))];
}
