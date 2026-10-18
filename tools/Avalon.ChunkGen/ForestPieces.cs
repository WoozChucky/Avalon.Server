namespace Avalon.ChunkGen;

/// <summary>
/// The forest's generated pieces (forest content pass): eight 1x1 pieces and three 2x2 set pieces. Boxes stand for
/// rocks and ruined walls, cylinders for tree clusters and pillars, each with the <see cref="Dressing" /> the forest
/// dresser draws on it; free-standing trees along the edges stand on their own trunk cylinders, and fallen logs and
/// leaning rocks on their models' footprints (forest dressing, #1109). Every slot is at least 2.5 m from any blocker
/// and every exit's throat is clear (ForestPiecesShould).
/// Each single piece has two doodad slots and one large one, each set-piece member one doodad slot (the arena's members
/// a large one too), off the paths and clear of blockers and spawn slots (a set piece's in its 60 x 60 m frame, where
/// its paths cross the inner edges), each yaw the quarter turn that best faces the piece's (or the set piece's) centre.
/// </summary>
public static class ForestPieces
{
    private const TreeSpecies Common = TreeSpecies.CommonTree, Pine = TreeSpecies.Pine, Dead = TreeSpecies.DeadTree;
    private const string Log1 = "avalon_log_fallen_1", Log2 = "avalon_log_fallen_2";

    private static CylinderBlocker Drum(float x, float z, float r) => new(x, z, r) { Dressing = Dressing.TreeDrum };
    private static CylinderBlocker Pillar(float x, float z, float r) => new(x, z, r) { Dressing = Dressing.Pillar };
    private static CylinderBlocker Stones(float x, float z, float r) => new(x, z, r) { Dressing = Dressing.Rock };
    private static BoxBlocker Rocks(float minX, float maxX, float minZ, float maxZ) => new(minX, maxX, minZ, maxZ) { Dressing = Dressing.Rock };
    private static BoxBlocker Wall(float minX, float maxX, float minZ, float maxZ) => new(minX, maxX, minZ, maxZ) { Dressing = Dressing.RuinWall };
    private static CylinderBlocker Tree(TreeSpecies species, float scale, float x, float z) => ForestDresser.Trunk(species, scale, x, z);
    private static IReadOnlyList<Blocker> Prop(string model, float x, float z, int yaw) => ForestDresser.Prop(model, x, z, yaw);
    private static Slot Pack(float x, float z) => new("pack", x, z);
    private static Slot Rare(float x, float z) => new("rare", x, z);
    private static Slot Leader(float x, float z) => new("leader", x, z);
    private static Slot Boss(float x, float z) => new("boss", x, z);
    private static DoodadSlot Doodad(float x, float z, int yaw) => new("doodad", x, z, yaw);
    private static DoodadSlot LargeDoodad(float x, float z, int yaw) => new("doodad_large", x, z, yaw);

    public static IReadOnlyList<ChunkPiece> Singles() =>
    [
        new("forest_thicket_01", [Side.N, Side.S],
            [Drum(6, 6, 2.5f), Drum(24, 8, 2), Drum(6, 23, 2), Drum(25, 24, 2.5f),
             Tree(Pine, 1.3f, 28.2f, 1f), Tree(Pine, 1.3f, 1f, 28.2f), Tree(Pine, 1.5f, 28.8f, 12.5f), Tree(Pine, 1.5f, 9f, 28.8f),
             Tree(Pine, 1.5f, 28.8f, 18.5f), Tree(Pine, 1.3f, 1.5f, 1.2f), Tree(Pine, 1.4f, 28.8f, 29f), Tree(Pine, 1.1f, 21f, 28.8f),
             .. Prop(Log1, 23.5f, 15.5f, 90)],
            [Pack(10, 15), Pack(20, 15), Pack(15, 9), Pack(15, 21)], ["path", "forest", "thicket"])
            { DoodadSlots = [LargeDoodad(3, 15, 90), Doodad(27, 15.5f, 270), Doodad(20.5f, 3, 0)], Hints = new(TreeMix.Pine) },
        new("forest_thicket_02", [Side.E, Side.S],
            [Drum(7, 24, 3), Drum(23, 25, 2), Rocks(3, 9, 3, 8),
             Tree(Pine, 1.5f, 28.8f, 8.5f), Tree(Pine, 1.3f, 21f, 1.2f), Tree(Dead, 1.1f, 28.8f, 21f), Tree(Pine, 1.5f, 29f, 28.2f),
             Tree(Pine, 1.2f, 1f, 28.8f), Tree(Pine, 1.2f, 1.2f, 19f), Tree(Pine, 1.2f, 1.2f, 12f), Tree(Pine, 1.4f, 12f, 28.8f),
             .. Prop(Log1, 7f, 15f, 90)],
            [Pack(15, 15), Pack(22, 9), Pack(11, 19), Pack(20, 20)], ["path", "forest", "thicket"])
            { DoodadSlots = [LargeDoodad(27, 3, 0), Doodad(3, 15, 90), Doodad(15.5f, 27, 180)], Hints = new(TreeMix.Pine) },
        new("forest_rocks_01", [Side.E, Side.W],
            [Rocks(4, 10, 22, 27), Rocks(20, 27, 21, 26), Rocks(12, 17, 3, 7),
             Tree(Pine, 1.2f, 6.5f, 1.2f), Tree(Common, 1.3f, 15.5f, 28.8f), Tree(Common, 1.1f, 21.5f, 1.2f), Tree(Common, 1.2f, 28.8f, 8.5f),
             Tree(Pine, 1.5f, 1.2f, 29f), Tree(Pine, 1.1f, 28.2f, 29f), Tree(Pine, 1.3f, 1.2f, 21f),
             .. Prop(Log2, 22.5f, 7f, 0), .. Prop("avalon_rock_leaning_2", 3f, 7.5f, 0)],
            [Pack(8, 15), Pack(22, 15), Pack(15, 12), Pack(15, 19)], ["path", "forest", "rocks"])
            { DoodadSlots = [LargeDoodad(27, 3, 0), Doodad(3, 3, 0), Doodad(15, 24.5f, 180)] },
        new("forest_rocks_02", [Side.N, Side.E, Side.S],
            [Rocks(3, 9, 3, 9), Rocks(3, 9, 21, 27), Drum(24, 24, 2),
             Tree(Common, 1.3f, 28.8f, 8f), Tree(Pine, 1.4f, 21f, 1.2f), Tree(Pine, 1.3f, 28.8f, 29f), Tree(Pine, 1.5f, 21f, 28.8f),
             Tree(Common, 1.2f, 28.8f, 21f), Tree(Pine, 1.2f, 1.2f, 18f), Tree(Pine, 1.3f, 1f, 1.2f), Tree(Pine, 1.4f, 1.2f, 11f),
             .. Prop(Log1, 6.5f, 14.5f, 90)],
            [Pack(15, 15), Pack(21, 9), Pack(14, 22), Pack(21, 18)], ["junction", "forest", "rocks"])
            { DoodadSlots = [LargeDoodad(27, 3, 0), Doodad(3, 15, 90), Doodad(10, 12.5f, 90)] },
        new("forest_ruin_01", [Side.N, Side.E, Side.S, Side.W],
            [Wall(2, 9, 2, 4), Wall(2, 4, 4, 9), Wall(21, 28, 26, 28), Wall(26, 28, 21, 26), Pillar(24, 6, 1.5f), Pillar(6, 24, 1.5f),
             Tree(Pine, 1.2f, 28.8f, 7.5f), Tree(Pine, 1.1f, 29f, 1.2f), Tree(Pine, 1.4f, 21f, 1.2f),
             .. Prop(Log2, 25f, 2f, 0)],
            [Pack(15, 15), Pack(10, 10), Pack(20, 20), Pack(20, 10), Rare(10, 20)], ["junction", "forest", "ruins"])
            { DoodadSlots = [LargeDoodad(2, 28, 180), Doodad(3, 20.5f, 90), Doodad(9.5f, 27, 180)], Hints = new(TreeMix.Old) },
        new("forest_ruin_02", [Side.N, Side.S],
            [Pillar(8, 8, 1.5f), Pillar(22, 8, 1.5f), Pillar(8, 22, 1.5f), Pillar(22, 22, 1.5f), Wall(2, 6, 13, 17), Wall(24, 28, 13, 17),
             Tree(Pine, 1.2f, 6f, 28.8f), Tree(Pine, 1.5f, 28.8f, 6f), Tree(Pine, 1.5f, 1.2f, 24f), Tree(Pine, 1.2f, 23.5f, 1.2f),
             Tree(Pine, 1.6f, 21f, 28.8f), Tree(Common, 1.2f, 9f, 1.2f), Tree(Pine, 1.2f, 1.2f, 8f), Tree(Pine, 1.1f, 26f, 28.8f),
             .. Prop(Log1, 28f, 22.5f, 90)],
            [Pack(15, 8), Pack(15, 22), Pack(11, 15), Pack(19, 15), Rare(15, 15)], ["path", "forest", "ruins"])
            { DoodadSlots = [LargeDoodad(3, 3, 0), Doodad(3, 27, 180), Doodad(27, 3, 0)], Hints = new(TreeMix.Old) },
        new("forest_hollow_01", [Side.S],
            [Drum(5, 10, 2), Drum(5, 20, 2), Drum(10, 26, 2), Drum(20, 26, 2), Drum(25, 20, 2), Drum(25, 10, 2),
             Tree(Pine, 1.2f, 28.8f, 1f), Tree(Pine, 1.3f, 1.8f, 1f), Tree(Pine, 1.3f, 28.8f, 28.5f), Tree(Pine, 1.1f, 28.8f, 15f),
             Tree(Pine, 1.4f, 1.2f, 15f), Tree(Pine, 1.2f, 28.8f, 6f), Tree(Pine, 1.6f, 28.8f, 24f), Tree(Pine, 1.1f, 1.2f, 6f),
             .. Prop(Log2, 15f, 28f, 0)],
            [Pack(10, 15), Pack(20, 15), Pack(15, 21), Pack(15, 9), Rare(15, 15)], ["deadend", "forest", "thicket"])
            { DoodadSlots = [LargeDoodad(8, 3, 0), Doodad(22, 3, 0), Doodad(3, 27, 180)], Hints = new(TreeMix.Pine) },
        new("forest_glade_01", [Side.N, Side.E],
            [Rocks(2, 9, 2, 9), Drum(6, 24, 2.5f), Drum(24, 6, 2.5f), Drum(24, 24, 2),
             Tree(Pine, 1.2f, 28.8f, 29f), Tree(Pine, 1.1f, 28.8f, 1f), Tree(Common, 1.2f, 19f, 1.2f), Tree(Pine, 1.2f, 1.8f, 29f),
             Tree(Pine, 1.4f, 1.2f, 20.5f), Tree(Pine, 1.2f, 28.8f, 21.5f), Tree(Common, 1.3f, 9f, 28.8f),
             .. Prop(Log1, 15f, 7f, 0), .. Prop("avalon_rock_leaning_1", 21.6f, 28.2f, 0)],
            [Pack(15, 15), Pack(11, 18), Pack(18, 11), Pack(20, 20)], ["path", "forest", "glade"])
            { DoodadSlots = [LargeDoodad(3, 15.5f, 90), Doodad(15.5f, 3, 0), Doodad(10.5f, 12.5f, 90)], Hints = new(TreeMix.Broadleaf) },
    ];

    public static IReadOnlyList<ChunkGroupPiece> Groups() =>
    [
        new("forest_clearing_big", [(0, 0, Side.S), (1, 0, Side.E), (1, 1, Side.N), (0, 1, Side.W)],
            [Drum(6, 6, 3), Drum(54, 6, 3), Drum(6, 54, 3), Drum(54, 54, 3),
             Tree(Pine, 1.3f, 58.8f, 29f), Tree(Common, 1.2f, 31.5f, 58.8f), Tree(Common, 1.2f, 1.8f, 39f), Tree(Pine, 1.2f, 58.8f, 41f),
             Tree(Common, 1.2f, 19.5f, 58.2f), Tree(Pine, 1.2f, 43.5f, 1.2f), Tree(Common, 1.4f, 58.2f, 21f), Tree(Common, 1.2f, 1.2f, 21f),
             Tree(Pine, 1.1f, 38.5f, 58.8f), Tree(Pine, 1.3f, 1.2f, 31.5f), Tree(Common, 1.4f, 1.8f, 14f), Tree(Pine, 1.6f, 58.8f, 35f),
             Tree(Pine, 1.2f, 37.5f, 1.2f), Tree(Pine, 1.5f, 21f, 1.2f), Tree(Pine, 1.3f, 29f, 1.2f), Tree(Common, 1.2f, 12f, 58.8f),
             Tree(Pine, 1.3f, 58.8f, 48f), Tree(Common, 1.2f, 25f, 58.8f), Tree(Pine, 1.3f, 58.8f, 1f), Tree(Pine, 1.3f, 49f, 1.2f),
             Tree(Common, 1.2f, 1.5f, 58.8f), Tree(Pine, 1.5f, 58.5f, 58.8f), Tree(Pine, 1.3f, 2.5f, 1.2f), Tree(Common, 1.2f, 51f, 58.8f),
             Tree(Common, 1.2f, 8.5f, 1.2f), Tree(Common, 1.3f, 58.8f, 8f), Tree(Common, 1.2f, 1.2f, 7f), Tree(Common, 1.3f, 1.8f, 51f),
             .. Prop(Log2, 35f, 6.5f, 0), .. Prop(Log2, 7f, 26.5f, 0), .. Prop(Log1, 25f, 7f, 90)],
            [Pack(14, 22), Pack(22, 14), Pack(46, 22), Pack(38, 14), Pack(14, 38), Pack(22, 46), Pack(46, 38), Pack(38, 46),
             Leader(40, 20), Leader(20, 40)],
            ["forest", "setpiece", "clearing"])
        {
            DoodadSlots = [Doodad(3, 17.5f, 90), Doodad(33, 3, 0), Doodad(15.5f, 57, 180), Doodad(57, 44.5f, 270)],
            Hints = new(TreeMix.Broadleaf),
        },
        new("forest_grove_ruin", [(0, 0, Side.S), (1, 0, Side.E), (1, 1, Side.N)],
            [Pillar(20, 20, 1.5f), Pillar(40, 20, 1.5f), Pillar(20, 40, 1.5f), Pillar(40, 40, 1.5f), Wall(4, 12, 50, 54), Wall(48, 52, 3, 9),
             Tree(Common, 1.2f, 58.8f, 53.5f), Tree(Pine, 1.2f, 31f, 58.8f), Tree(Dead, 1.1f, 58.8f, 28.5f), Tree(Pine, 1.4f, 29f, 1.2f),
             Tree(Dead, 1.5f, 58.2f, 41f), Tree(Common, 1.3f, 21f, 58.8f), Tree(Pine, 1.3f, 39f, 1.8f), Tree(Common, 1.2f, 51.5f, 58.8f),
             Tree(Common, 1.1f, 1.2f, 42.5f), Tree(Common, 1.1f, 21f, 1.2f), Tree(Pine, 1.4f, 59f, 2.5f), Tree(Common, 1.2f, 9f, 1.8f),
             Tree(Pine, 1.4f, 39f, 57.5f), Tree(Common, 1.1f, 1.2f, 27.5f), Tree(Pine, 1.4f, 58.2f, 21f), Tree(Pine, 1.3f, 58.8f, 9f),
             Tree(Common, 1.2f, 1.2f, 17.5f), Tree(Pine, 1.2f, 1.5f, 58.8f), Tree(Pine, 1.1f, 1.2f, 6f), Tree(Common, 1.2f, 58.8f, 47.5f),
             Tree(Common, 1.2f, 13.5f, 58.8f), Tree(Common, 1.3f, 7f, 58.8f), Tree(Common, 1.2f, 1.2f, 11.5f), Tree(Pine, 1.2f, 1.2f, 36.5f),
             Tree(Common, 1.4f, 58.2f, 34.5f), Tree(Common, 1.1f, 44.5f, 1.2f), Tree(Pine, 1.3f, 1.2f, 48.5f), Tree(Pine, 1.4f, 26f, 56.5f),
             .. Prop(Log1, 6.5f, 34f, 90), .. Prop(Log2, 7f, 21.5f, 90), .. Prop(Log1, 6.5f, 11f, 90)],
            [Pack(10, 12), Pack(24, 10), Pack(36, 10), Pack(50, 24), Pack(10, 46), Pack(24, 52), Pack(36, 52), Pack(50, 48),
             Leader(45, 40), Leader(15, 40), Rare(45, 15)],
            ["forest", "setpiece", "ruins"])
        {
            DoodadSlots = [Doodad(3, 3, 90), Doodad(33, 16.5f, 0), Doodad(3, 33, 90), Doodad(57, 57, 270)],
            Hints = new(TreeMix.Old),
        },
        new("forest_arena", [(0, 0, Side.S)],
            [Stones(16, 16, 1.5f), Stones(44, 16, 1.5f), Stones(16, 44, 1.5f), Stones(44, 44, 1.5f),
             Rocks(2, 8, 52, 58), Rocks(52, 58, 52, 58), Rocks(52, 58, 2, 8),
             Tree(Common, 1.2f, 28.5f, 58.8f), Tree(Pine, 1.2f, 28f, 1.2f), Tree(Pine, 1.5f, 1.2f, 38f), Tree(Pine, 1.4f, 58.8f, 38.5f),
             Tree(Pine, 1.3f, 40f, 1.2f), Tree(Pine, 1.5f, 9f, 1.2f), Tree(Pine, 1.4f, 43.5f, 58.8f), Tree(Pine, 1.4f, 58.8f, 15.5f),
             Tree(Pine, 1.5f, 58.8f, 31f), Tree(Pine, 1.6f, 21f, 1.2f), Tree(Common, 1.2f, 58.8f, 45.5f), Tree(Pine, 1.3f, 36f, 58.2f),
             Tree(Common, 1.2f, 1.2f, 28f), Tree(Common, 1.1f, 14f, 58.8f), Tree(Common, 1.3f, 1.8f, 45.5f), Tree(Pine, 1.3f, 1.2f, 8f),
             Tree(Common, 1.2f, 22f, 58.8f), Tree(Common, 1.1f, 1.2f, 18.5f), Tree(Common, 1.1f, 34f, 1.2f), Tree(Common, 1.3f, 58.8f, 21.5f),
             Tree(Pine, 1.6f, 46.5f, 1.8f), Tree(Common, 1.2f, 49f, 58.8f), Tree(Pine, 1.2f, 3.5f, 33f), Tree(Common, 1.1f, 58.8f, 10f),
             .. Prop(Log1, 53f, 23.5f, 90), .. Prop(Log2, 6.5f, 13.5f, 90), .. Prop(Log2, 7f, 23f, 90)],
            [Boss(36, 36), Leader(38, 22), Leader(22, 38),
             Pack(15, 10), Pack(24, 22), Pack(48, 10), Pack(40, 8), Pack(10, 48), Pack(8, 40)],
            ["forest", "setpiece", "boss", "arena"])
        {
            DoodadSlots = [LargeDoodad(3, 3, 90), Doodad(3, 15, 90), LargeDoodad(47.5f, 27, 270), Doodad(33, 14, 0),
                LargeDoodad(18, 55.5f, 180), Doodad(14, 33, 90), LargeDoodad(33, 47.5f, 180), Doodad(40.5f, 57, 180)],
        },
    ];
}
