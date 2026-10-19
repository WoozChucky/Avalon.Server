using System.Globalization;
using System.Numerics;

namespace Avalon.World.ChunkLayouts;

/// <summary>
/// What a pool piece is for: a border fills a ring cell no exit faces, a cap ends the paths of the open exits that face
/// its cell, and a town-ward piece fills one fixed spot around the entry chunk.
/// </summary>
public enum SceneryKind
{
    Border,
    Cap,
    TownWard,
}

/// <summary>
/// One piece of a map's scenery pool. <paramref name="Front" /> is used by borders (the side turned toward the layout),
/// <paramref name="CappedSides" /> by caps (a <see cref="ScenerySides" /> mask in the piece's own frame),
/// <paramref name="Filler" /> by borders (a filler may repeat beside itself) and <paramref name="SpotX" />/
/// <paramref name="SpotZ" /> by town-ward pieces (the spot's offset from the entry chunk); a kind's unused fields are
/// 0 or false. <paramref name="Weight" /> is the draw weight of a border or a cap.
/// </summary>
public sealed record SceneryPoolPiece(
    string Name,
    SceneryKind Kind,
    float Weight,
    ExitSide Front,
    byte CappedSides,
    bool Filler,
    int SpotX,
    int SpotZ);

/// <summary>
/// A map's validated scenery pool. The constructor refuses (<see cref="InvalidDataException" />) a piece name that is
/// empty, not <c>[a-z0-9_]+</c> or listed twice; a border or cap whose weight is not a finite number above 0; a border
/// whose front is none of N, E, S, W; a cap that caps no side (mask 0 or above 15); a pool without a cap of every shape
/// (one side, two adjacent, two opposite, three, four, whatever the rotation); a pool without a filler border; and
/// town-ward pieces that are neither absent nor exactly one per spot of (-1..1, -1..1) minus (0, 0).
/// </summary>
public sealed class SceneryPool
{
    private static readonly (string Shape, Func<byte, bool> Matches)[] s_capShapes =
    [
        ("one capped side", mask => BitCount(mask) == 1),
        ("two adjacent capped sides", mask => BitCount(mask) == 2 && !IsOpposite(mask)),
        ("two opposite capped sides", IsOpposite),
        ("three capped sides", mask => BitCount(mask) == 3),
        ("four capped sides", mask => BitCount(mask) == 4),
    ];

    public SceneryPool(IReadOnlyList<SceneryPoolPiece> pieces)
    {
        ArgumentNullException.ThrowIfNull(pieces);

        HashSet<string> names = new(StringComparer.Ordinal);
        for (int index = 0; index < pieces.Count; index++)
        {
            SceneryPoolPiece piece = pieces[index];
            if (!IsPlainName(piece.Name))
                throw PieceRefusal(index, $"'{piece.Name}' is not a plain piece name ([a-z0-9_]+)");
            if (!names.Add(piece.Name))
                throw PieceRefusal(index, $"'{piece.Name}' is listed twice");
            if (piece.Kind is SceneryKind.Border or SceneryKind.Cap && !(float.IsFinite(piece.Weight) && piece.Weight > 0))
            {
                throw PieceRefusal(index,
                    $"'{piece.Name}' has weight {piece.Weight.ToString(CultureInfo.InvariantCulture)}, not a finite number above 0");
            }

            if (piece.Kind == SceneryKind.Border && !Enum.IsDefined(piece.Front))
                throw PieceRefusal(index, $"border '{piece.Name}' has front {(int)piece.Front}, none of N, E, S, W");
            if (piece.Kind == SceneryKind.Cap && piece.CappedSides is 0 or > 0b1111)
                throw PieceRefusal(index, $"cap '{piece.Name}' caps no side: its capped-side mask {piece.CappedSides} is not 1-15");
        }

        foreach ((string shape, Func<byte, bool> matches) in s_capShapes)
        {
            if (!pieces.Any(p => p.Kind == SceneryKind.Cap && matches(p.CappedSides)))
                throw new InvalidDataException($"the pool has no cap for {shape}");
        }

        if (!pieces.Any(p => p.Kind == SceneryKind.Border && p.Filler))
            throw new InvalidDataException("the pool has no filler border piece");

        CheckTownWard(pieces);
        Pieces = pieces.ToArray();
    }

    /// <summary>Every piece, in the order given.</summary>
    public IReadOnlyList<SceneryPoolPiece> Pieces { get; }

    private static void CheckTownWard(IReadOnlyList<SceneryPoolPiece> pieces)
    {
        HashSet<(int X, int Z)> held = [];
        for (int index = 0; index < pieces.Count; index++)
        {
            SceneryPoolPiece piece = pieces[index];
            if (piece.Kind != SceneryKind.TownWard)
                continue;

            (int X, int Z) spot = (piece.SpotX, piece.SpotZ);
            if (spot.X is < -1 or > 1 || spot.Z is < -1 or > 1 || spot == (0, 0))
            {
                throw PieceRefusal(index,
                    $"'{piece.Name}' sits at ({spot.X}, {spot.Z}), none of the town-ward spots (-1..1, -1..1) minus (0, 0)");
            }

            if (!held.Add(spot))
            {
                throw PieceRefusal(index,
                    $"'{piece.Name}' holds ({spot.X}, {spot.Z}) a second time: the town-ward spots take one piece each");
            }
        }

        if (held.Count is not (0 or 8))
        {
            throw new InvalidDataException(
                $"the town-ward spots take a piece each or none at all, but only {held.Count} of the 8 have one");
        }
    }

    /// <summary>
    /// The <see cref="Exception.Data" /> key of a refusal of one piece: its index in the pieces given, so the ring file
    /// reader can name the entry (<c>pool[3]</c>) as it does for its own refusals.
    /// </summary>
    internal const string PieceIndexKey = "SceneryPool.PieceIndex";

    private static InvalidDataException PieceRefusal(int index, string message) =>
        new(message) { Data = { [PieceIndexKey] = index } };

    /// <summary>A piece name is <c>[a-z0-9_]+</c>: it names files, so it holds nothing else.</summary>
    internal static bool IsPlainName(string name) =>
        !string.IsNullOrEmpty(name) && name.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_');

    private static int BitCount(byte mask) => BitOperations.PopCount(mask);

    private static bool IsOpposite(byte mask) => mask is 0b0101 or 0b1010;
}

/// <summary>
/// 4-bit side masks: N 1, E 2, S 4, W 8 (<see cref="ExitSide" /> n is bit n). A rotation turns every side by quarter
/// turns, N to E for 1, as <see cref="ExitMask.Rotate" /> and <see cref="ChunkRotation" /> do.
/// </summary>
public static class ScenerySides
{
    public static byte Bit(ExitSide side) => (byte)(1 << (int)side);

    public static byte Rotate(byte mask, int rotation)
    {
        rotation &= 0b11;
        byte result = 0;
        for (int s = 0; s < 4; s++)
        {
            if ((mask & (1 << s)) != 0)
                result |= (byte)(1 << ((s + rotation) & 0b11));
        }

        return result;
    }

    /// <summary>The mask's sides as letters in N, E, S, W order (<c>"SW"</c>); empty for no side.</summary>
    public static string Name(byte mask)
    {
        Span<char> letters = stackalloc char[4];
        int count = 0;
        for (int s = 0; s < 4; s++)
        {
            if ((mask & (1 << s)) != 0)
                letters[count++] = "NESW"[s];
        }

        return new string(letters[..count]);
    }
}
