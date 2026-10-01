namespace Avalon.ChunkGen;

/// <summary>
/// The forest's generated pieces (forest content pass): eight 1x1 pieces and three 2x2 set pieces. Boxes stand for
/// rocks and ruined walls, cylinders for tree clusters and pillars; placeholders until real art exists. Every slot is
/// at least 2.5 m from any blocker and every exit's throat is clear (ForestPiecesShould).
/// </summary>
public static class ForestPieces
{
    private static CylinderBlocker Cyl(float x, float z, float r) => new(x, z, r);
    private static BoxBlocker Box(float minX, float maxX, float minZ, float maxZ) => new(minX, maxX, minZ, maxZ);
    private static Slot Pack(float x, float z) => new("pack", x, z);
    private static Slot Rare(float x, float z) => new("rare", x, z);
    private static Slot Leader(float x, float z) => new("leader", x, z);
    private static Slot Boss(float x, float z) => new("boss", x, z);

    public static IReadOnlyList<ChunkPiece> Singles() =>
    [
        new("forest_thicket_01", [Side.N, Side.S],
            [Cyl(6, 6, 2.5f), Cyl(24, 8, 2), Cyl(6, 23, 2), Cyl(25, 24, 2.5f)],
            [Pack(10, 15), Pack(20, 15), Pack(15, 9), Pack(15, 21)], ["path", "forest", "thicket"]),
        new("forest_thicket_02", [Side.E, Side.S],
            [Cyl(7, 24, 3), Cyl(23, 25, 2), Box(3, 9, 3, 8)],
            [Pack(15, 15), Pack(22, 9), Pack(11, 19), Pack(20, 20)], ["path", "forest", "thicket"]),
        new("forest_rocks_01", [Side.E, Side.W],
            [Box(4, 10, 22, 27), Box(20, 27, 21, 26), Box(12, 17, 3, 7)],
            [Pack(8, 15), Pack(22, 15), Pack(15, 12), Pack(15, 19)], ["path", "forest", "rocks"]),
        new("forest_rocks_02", [Side.N, Side.E, Side.S],
            [Box(3, 9, 3, 9), Box(3, 9, 21, 27), Cyl(24, 24, 2)],
            [Pack(15, 15), Pack(21, 9), Pack(14, 22), Pack(21, 18)], ["junction", "forest", "rocks"]),
        new("forest_ruin_01", [Side.N, Side.E, Side.S, Side.W],
            [Box(2, 9, 2, 4), Box(2, 4, 4, 9), Box(21, 28, 26, 28), Box(26, 28, 21, 26), Cyl(24, 6, 1.5f), Cyl(6, 24, 1.5f)],
            [Pack(15, 15), Pack(10, 10), Pack(20, 20), Pack(20, 10), Rare(10, 20)], ["junction", "forest", "ruins"]),
        new("forest_ruin_02", [Side.N, Side.S],
            [Cyl(8, 8, 1.5f), Cyl(22, 8, 1.5f), Cyl(8, 22, 1.5f), Cyl(22, 22, 1.5f), Box(2, 6, 13, 17), Box(24, 28, 13, 17)],
            [Pack(15, 8), Pack(15, 22), Pack(11, 15), Pack(19, 15), Rare(15, 15)], ["path", "forest", "ruins"]),
        new("forest_hollow_01", [Side.S],
            [Cyl(5, 10, 2), Cyl(5, 20, 2), Cyl(10, 26, 2), Cyl(20, 26, 2), Cyl(25, 20, 2), Cyl(25, 10, 2)],
            [Pack(10, 15), Pack(20, 15), Pack(15, 21), Pack(15, 9), Rare(15, 15)], ["deadend", "forest", "thicket"]),
        new("forest_glade_01", [Side.N, Side.E],
            [Box(2, 9, 2, 9), Cyl(6, 24, 2.5f), Cyl(24, 6, 2.5f), Cyl(24, 24, 2)],
            [Pack(15, 15), Pack(11, 18), Pack(18, 11), Pack(20, 20)], ["path", "forest", "glade"]),
    ];

    public static IReadOnlyList<ChunkGroupPiece> Groups() =>
    [
        new("forest_clearing_big", [(0, 0, Side.S), (1, 0, Side.E), (1, 1, Side.N), (0, 1, Side.W)],
            [Cyl(6, 6, 3), Cyl(54, 6, 3), Cyl(6, 54, 3), Cyl(54, 54, 3)],
            [Pack(14, 22), Pack(22, 14), Pack(46, 22), Pack(38, 14), Pack(14, 38), Pack(22, 46), Pack(46, 38), Pack(38, 46),
             Leader(40, 20), Leader(20, 40)],
            ["forest", "setpiece", "clearing"]),
        new("forest_grove_ruin", [(0, 0, Side.S), (1, 0, Side.E), (1, 1, Side.N)],
            [Cyl(20, 20, 1.5f), Cyl(40, 20, 1.5f), Cyl(20, 40, 1.5f), Cyl(40, 40, 1.5f), Box(4, 12, 50, 54), Box(48, 52, 3, 9)],
            [Pack(10, 12), Pack(24, 10), Pack(36, 10), Pack(50, 24), Pack(10, 46), Pack(24, 52), Pack(36, 52), Pack(50, 48),
             Leader(45, 40), Leader(15, 40), Rare(45, 15)],
            ["forest", "setpiece", "ruins"]),
        new("forest_arena", [(0, 0, Side.S)],
            [Cyl(16, 16, 1.5f), Cyl(44, 16, 1.5f), Cyl(16, 44, 1.5f), Cyl(44, 44, 1.5f),
             Box(2, 8, 52, 58), Box(52, 58, 52, 58), Box(52, 58, 2, 8)],
            [Boss(36, 36), Leader(38, 22), Leader(22, 38),
             Pack(15, 10), Pack(24, 22), Pack(48, 10), Pack(40, 8), Pack(10, 48), Pack(8, 40)],
            ["forest", "setpiece", "boss", "arena"]),
    ];
}
