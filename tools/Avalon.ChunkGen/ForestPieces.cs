namespace Avalon.ChunkGen;

/// <summary>
/// The forest's generated pieces: thirty-two 1x1 pieces and three 2x2 set pieces. The first nine (the entry, the
/// paths, the junctions, the small clearing and the dead end) were floors written by hand until forest dressing
/// (#1109) ported them, with their exits, spawn slots, tags and the entry's portal as they were; the last fifteen
/// came with it, two of them (tagged overhang) carrying the rock arch and the rock overhang. Boxes stand for rocks
/// and ruined walls, cylinders for tree clusters and pillars, each with the <see cref="Dressing" /> the forest dresser
/// draws on it; fallen logs and custom rocks stand on their models' footprints. The wood around a piece is its tree
/// walls: a TreeWall box about 3.5 m deep along every closed side (and, in a piece with at most one closed side, in the
/// corners between its exits; along a set piece's closed outer edges, a box in each cell), shallower or shorter where
/// a drum, a box, a log or a slot would be too near, and packed with trees by the dresser; the few free-standing
/// trees left (on the exit sides, and inside) stand on their own trunk cylinders. Every slot is at least 2.5 m from
/// any blocker and every exit's throat is clear (ForestPiecesShould).
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
    private static BoxBlocker TreeWall(float minX, float maxX, float minZ, float maxZ) => new(minX, maxX, minZ, maxZ) { Dressing = Dressing.TreeWall };
    private static CylinderBlocker Tree(TreeSpecies species, float scale, float x, float z) => ForestDresser.Trunk(species, scale, x, z);
    private static IReadOnlyList<Blocker> Prop(string model, float x, float z, int yaw) => ForestDresser.Prop(model, x, z, yaw);
    private static Slot Pack(float x, float z) => new("pack", x, z);
    private static Slot Rare(float x, float z) => new("rare", x, z);
    private static Slot Leader(float x, float z) => new("leader", x, z);
    private static Slot Boss(float x, float z) => new("boss", x, z);
    private static Slot Entry(float x, float z) => new("entry", x, z);
    private static DoodadSlot Doodad(float x, float z) => new("doodad", x, z, Facing(x, z, ChunkPiece.CellSize / 2f));
    private static DoodadSlot LargeDoodad(float x, float z) => new("doodad_large", x, z, Facing(x, z, ChunkPiece.CellSize / 2f));
    private static DoodadSlot SetDoodad(float x, float z) => new("doodad", x, z, Facing(x, z, ChunkPiece.CellSize));
    private static DoodadSlot SetLargeDoodad(float x, float z) => new("doodad_large", x, z, Facing(x, z, ChunkPiece.CellSize));

    /// <summary>
    /// The quarter turn (0 faces +Z, 90 faces +X) that best faces (centre, centre) from (x, z): a single piece's centre,
    /// or its set piece's; a tie goes to 0 or 180.
    /// </summary>
    internal static int Facing(float x, float z, float centre)
    {
        float dx = centre - x, dz = centre - z;
        return MathF.Abs(dz) >= MathF.Abs(dx) ? (dz >= 0f ? 0 : 180) : (dx > 0f ? 90 : 270);
    }

    public static IReadOnlyList<ChunkPiece> Singles() =>
    [
        // The entry: the way in from the north and the way back to town at (15, 5). Its doodad slots stand in the
        // corners, the only spots of it 15 m from the entry spawn and 8 m from the portal (DoodadPlacementService), so
        // its tree walls stop short of them, and the south wall is shallower behind the portal.
        new("forest_entry_01", [Side.N],
            [Drum(5, 23, 2.5f), .. Prop("avalon_rock_large_2", 6f, 9f, 0), Tree(Pine, 1.5f, 21.5f, 28.8f), .. Prop(Log2, 2f, 8.5f, 90),
             TreeWall(5.6f, 12.8f, 0.1f, 3.6f), TreeWall(12.8f, 17.2f, 0.1f, 2.1f), TreeWall(17.2f, 24.4f, 0.1f, 3.6f),
             TreeWall(0.1f, 3.6f, 10.9f, 20.5f), TreeWall(0.1f, 2.1f, 20.5f, 29.9f), TreeWall(26.4f, 29.9f, 5.6f, 22.9f)],
            [Entry(15, 15)], ["entry", "forest"])
        {
            DoodadSlots = [LargeDoodad(27, 27), Doodad(3, 3), Doodad(27, 3)], PortalSlots = [new("Back", 15, 5)],
            Hints = new(TreeMix.Broadleaf),
        },
        new("forest_path_01", [Side.N, Side.S],
            [Drum(5, 5, 2.5f), Drum(24, 24, 2), Tree(Pine, 1.1f, 21f, 1.2f), .. Prop(Log1, 4f, 28f, 0), TreeWall(0.1f, 2.1f, 0.1f, 7.5f),
             TreeWall(0.1f, 3.6f, 7.5f, 27), TreeWall(26.4f, 29.9f, 0.1f, 29.9f)],
            [Pack(10, 15), Pack(20, 15), Pack(15, 8), Pack(15, 22)], ["path", "forest"])
            { DoodadSlots = [LargeDoodad(22.3f, 11.6f), Doodad(6.2f, 14.5f), Doodad(9.5f, 27)], Hints = new(TreeMix.Pine) },
        new("forest_path_02", [Side.E, Side.S],
            [Drum(7, 23, 2.5f), .. Prop("avalon_rock_large_2", 22f, 24f, 0), Tree(Common, 1.3f, 28.8f, 8f), Tree(Common, 1.1f, 22f, 1.2f),
             .. Prop(Log1, 3f, 2f, 0), TreeWall(0.1f, 21.3f, 26.4f, 29.9f), TreeWall(22.9f, 29.9f, 26.4f, 29.9f),
             TreeWall(0.1f, 3.6f, 2.9f, 26.4f)],
            [Pack(15, 10), Rare(15, 20), Pack(22, 10)], ["path", "forest"])
            { DoodadSlots = [LargeDoodad(27, 3), Doodad(6.2f, 15), Doodad(15.5f, 23.8f)] },
        new("forest_path_03", [Side.N, Side.E],
            [Drum(4, 14, 2), .. Prop("avalon_rock_large_2", 25f, 5f, 0), Tree(Pine, 1.4f, 8f, 28.8f), Tree(Common, 1.2f, 28.8f, 21f),
             Tree(Pine, 1.2f, 21.5f, 28.8f), Tree(Common, 1.2f, 28.8f, 9f), .. Prop(Log1, 8f, 2f, 0), TreeWall(0.1f, 5.2f, 0.1f, 3.6f),
             TreeWall(10.8f, 22.8f, 0.1f, 3.6f), TreeWall(22.8f, 29.9f, 0.1f, 2.1f), TreeWall(0.1f, 3.6f, 3.6f, 11.7f),
             TreeWall(0.1f, 3.6f, 16.3f, 29.9f)],
            [Pack(12, 18), Pack(20, 12), Pack(8, 8), Pack(22, 22)], ["path", "forest"])
            { DoodadSlots = [LargeDoodad(15, 7.7f), Doodad(6.2f, 22), Doodad(27, 27)] },
        new("forest_path_04", [Side.E, Side.W],
            [Drum(24, 25, 2.5f), .. Prop("avalon_rock_large_3", 6f, 5f, 0), Tree(Common, 1.2f, 28.8f, 7f), Tree(Pine, 1.5f, 1.2f, 22f),
             .. Prop(Log1, 21.5f, 2f, 0), TreeWall(0.1f, 9.3f, 0.1f, 2.1f), TreeWall(9.3f, 18.6f, 0.1f, 3.6f),
             TreeWall(24.3f, 29.9f, 0.1f, 3.6f), TreeWall(0.1f, 21.5f, 26.4f, 29.9f), TreeWall(21.5f, 29.9f, 27.9f, 29.9f)],
            [Pack(10, 15), Pack(20, 15), Pack(15, 8), Pack(15, 22)], ["path", "forest"])
            { DoodadSlots = [LargeDoodad(6, 22.3f), Doodad(25.4f, 6.2f), Doodad(12.5f, 23.8f)] },
        new("forest_junction_t", [Side.N, Side.E, Side.S],
            [Drum(4, 26, 2), .. Prop("avalon_rock_large_2", 4f, 4f, 0), Tree(Common, 1.2f, 9f, 28.8f), Tree(Common, 1.2f, 7.5f, 1.2f),
             .. Prop(Log1, 25f, 28f, 0), TreeWall(20.1f, 29.9f, 0.1f, 3.6f), TreeWall(0.1f, 3.6f, 6.5f, 23.7f),
             TreeWall(26.4f, 29.9f, 3.6f, 9.9f), TreeWall(26.4f, 29.9f, 20.1f, 27), TreeWall(27.9f, 29.9f, 27, 29.9f)],
            [Pack(15, 12), Pack(15, 18), Rare(22, 22), Pack(8, 8), Pack(8, 22)], ["junction", "forest"])
            { DoodadSlots = [LargeDoodad(22.3f, 7.7f), Doodad(6.2f, 15), Doodad(10, 12.5f)] },
        new("forest_junction_x", [Side.N, Side.E, Side.S, Side.W],
            [.. Prop("avalon_rock_large_1", 5.5f, 5.5f, 0), Tree(Common, 1.2f, 9f, 1.2f), .. Prop(Log1, 3.5f, 28f, 0),
             TreeWall(0.1f, 4.5f, 0.1f, 2.6f), TreeWall(20.1f, 24.4f, 0.1f, 3.6f), TreeWall(6.4f, 9.9f, 26.4f, 29.9f),
             TreeWall(20.1f, 22.9f, 26.4f, 29.9f), TreeWall(0.1f, 2.1f, 2.6f, 9.9f), TreeWall(0.1f, 3.6f, 20.1f, 27),
             TreeWall(26.4f, 29.9f, 5.6f, 9.9f), TreeWall(26.4f, 29.9f, 20.1f, 22.9f)],
            [Pack(15, 15), Rare(8, 22), Rare(22, 8), Pack(22, 22)], ["junction", "forest"])
            { DoodadSlots = [LargeDoodad(27, 27), Doodad(6.1f, 19.5f), Doodad(27, 3)] },
        new("forest_clearing_01", [Side.N, Side.S],
            [Drum(3.5f, 3.5f, 1.5f), Drum(26.5f, 3.5f, 1.5f), Drum(26.5f, 26.5f, 1.5f), Drum(3.5f, 26.5f, 1.5f), Tree(Pine, 1.2f, 9f, 1.2f),
             Tree(Pine, 1.2f, 21f, 1.2f), .. Prop(Log2, 27.5f, 11f, 0), TreeWall(0.1f, 3.6f, 5.4f, 10.9f),
             TreeWall(0.1f, 3.6f, 19.1f, 24.6f), TreeWall(26.4f, 29.9f, 5.4f, 10.2f), TreeWall(26.4f, 29.9f, 11.8f, 24.6f)],
            [Pack(8, 22), Pack(22, 22), Pack(8, 8), Pack(22, 8), Rare(15, 15), Pack(15, 8), Pack(15, 22), Pack(8, 15), Pack(22, 15)],
            ["clearing", "forest"])
            { DoodadSlots = [LargeDoodad(3, 15), Doodad(23.8f, 17.5f), Doodad(8, 27)], Hints = new(TreeMix.Broadleaf) },
        new("forest_deadend_01", [Side.S],
            [Drum(24, 20, 2.5f), Drum(8, 24, 2), .. Prop(Log1, 16f, 28f, 0), TreeWall(5.6f, 13.2f, 26.4f, 29.9f),
             TreeWall(18.8f, 29.9f, 26.4f, 29.9f), TreeWall(0.1f, 3.6f, 0.1f, 24.4f), TreeWall(26.4f, 29.9f, 0.1f, 18.5f),
             TreeWall(26.9f, 29.9f, 18.5f, 26.4f)],
            [Pack(15, 10), Rare(15, 22), Pack(8, 15)], ["deadend", "forest"])
            { DoodadSlots = [LargeDoodad(8, 3), Doodad(22, 3), Doodad(3, 27)], Hints = new(TreeMix.Pine) },
        new("forest_thicket_01", [Side.N, Side.S],
            [Drum(6, 6, 2.5f), Drum(24, 8, 2), Drum(6, 23, 2), Drum(25, 24, 2.5f), Tree(Pine, 1.5f, 9f, 28.8f),
             Tree(Pine, 1.1f, 21f, 28.8f), .. Prop(Log1, 23.5f, 15.5f, 90), TreeWall(0.1f, 3.1f, 0.1f, 10.9f),
             TreeWall(0.1f, 3.6f, 19.1f, 29.9f), TreeWall(26.4f, 29.9f, 0.1f, 12.9f), TreeWall(27.9f, 29.9f, 18.1f, 29.9f)],
            [Pack(10, 15), Pack(20, 15), Pack(15, 9), Pack(15, 21)], ["path", "forest", "thicket"])
            { DoodadSlots = [LargeDoodad(3, 15), Doodad(27, 15.5f), Doodad(20.5f, 3)], Hints = new(TreeMix.Pine) },
        new("forest_thicket_02", [Side.E, Side.S],
            [Drum(7, 24, 3), Drum(23, 25, 2), Rocks(3, 9, 3, 8), Tree(Pine, 1.5f, 28.8f, 8.5f), Tree(Pine, 1.3f, 21f, 1.2f),
             Tree(Dead, 1.1f, 28.8f, 21f), .. Prop(Log1, 7f, 15f, 90), TreeWall(0.1f, 9.3f, 27.4f, 29.9f),
             TreeWall(9.3f, 21.1f, 26.4f, 29.9f), TreeWall(21.1f, 29.9f, 27.4f, 29.9f), TreeWall(0.1f, 2.6f, 0.1f, 12.4f),
             TreeWall(0.1f, 3.6f, 17.6f, 27.4f)],
            [Pack(15, 15), Pack(22, 9), Pack(11, 19), Pack(20, 20)], ["path", "forest", "thicket"])
            { DoodadSlots = [LargeDoodad(27, 3), Doodad(3, 15), Doodad(15.5f, 23.8f)], Hints = new(TreeMix.Pine) },
        new("forest_rocks_01", [Side.E, Side.W],
            [Rocks(4, 10, 22, 27), Rocks(20, 27, 21, 26), Rocks(12, 17, 3, 7), Tree(Common, 1.2f, 28.8f, 8.5f), Tree(Pine, 1.3f, 1.2f, 21f),
             .. Prop(Log2, 22.5f, 7f, 0), .. Prop("avalon_rock_leaning_2", 3f, 7.5f, 0), TreeWall(0.1f, 11.7f, 0.1f, 3.6f),
             TreeWall(11.7f, 22.9f, 0.1f, 2.6f), TreeWall(0.1f, 3.6f, 26.4f, 29.9f), TreeWall(3.6f, 10.3f, 27.4f, 29.9f),
             TreeWall(10.3f, 29.9f, 26.4f, 29.9f)],
            [Pack(8, 15), Pack(22, 15), Pack(15, 12), Pack(15, 19)], ["path", "forest", "rocks"])
            { DoodadSlots = [LargeDoodad(27, 3), Doodad(6.8f, 6.2f), Doodad(15, 23.8f)] },
        new("forest_rocks_02", [Side.N, Side.E, Side.S],
            [Rocks(3, 9, 3, 9), Rocks(3, 9, 21, 27), Drum(24, 24, 2), .. Prop(Log1, 6.5f, 14.5f, 90), TreeWall(20.1f, 22.9f, 0.1f, 3.6f),
             TreeWall(20.1f, 29.9f, 26.4f, 29.9f), TreeWall(0.1f, 3.6f, 0.1f, 2.6f), TreeWall(0.1f, 2.6f, 2.6f, 9.3f),
             TreeWall(0.1f, 3.6f, 9.3f, 20.6f), TreeWall(0.1f, 2.6f, 20.6f, 27.3f), TreeWall(0.1f, 3.6f, 27.3f, 29.9f),
             TreeWall(26.4f, 29.9f, 7.1f, 9.9f), TreeWall(26.4f, 29.9f, 20.1f, 26.4f)],
            [Pack(15, 15), Pack(21, 9), Pack(14, 22), Pack(21, 18)], ["junction", "forest", "rocks"])
            { DoodadSlots = [LargeDoodad(27, 3), Doodad(9.7f, 15), Doodad(10, 12.5f)] },
        // A crossroads with no closed side: trees inside its two ruined corners and in its north-west corner make up
        // for the walls its corner ruins and slots leave little room for.
        new("forest_ruin_01", [Side.N, Side.E, Side.S, Side.W],
            [Wall(2, 9, 2, 4), Wall(2, 4, 4, 9), Wall(21, 28, 26, 28), Wall(26, 28, 21, 26), Pillar(24, 6, 1.5f), Pillar(6, 24, 1.5f),
             Tree(Common, 1.3f, 5.6f, 5.6f), Tree(Pine, 1.4f, 7.8f, 5.4f), Tree(Dead, 1.1f, 5.4f, 7.8f),
             Tree(Common, 1.2f, 24.4f, 24.4f), Tree(Pine, 1.3f, 22.2f, 24.6f), Tree(Common, 1.1f, 24.6f, 22.2f),
             Tree(Pine, 1.4f, 7.5f, 28.2f),
             .. Prop(Log2, 25f, 2f, 0), TreeWall(20.1f, 22.6f, 0.1f, 3.6f), TreeWall(27.4f, 29.9f, 0.1f, 3.6f),
             TreeWall(0.1f, 2.6f, 20.1f, 23.9f), TreeWall(26.4f, 29.9f, 3.6f, 9.9f)],
            [Pack(15, 15), Pack(10, 10), Pack(20, 20), Pack(20, 10), Rare(10, 20)], ["junction", "forest", "ruins"])
            { DoodadSlots = [LargeDoodad(2, 28), Doodad(6.2f, 19.9f), Doodad(10.1f, 23.8f)], Hints = new(TreeMix.Old) },
        new("forest_ruin_02", [Side.N, Side.S],
            [Pillar(8, 8, 1.5f), Pillar(22, 8, 1.5f), Pillar(8, 22, 1.5f), Pillar(22, 22, 1.5f), Wall(2, 6, 13, 17), Wall(24, 28, 13, 17),
             Tree(Pine, 1.2f, 6f, 28.8f), Tree(Pine, 1.2f, 23.5f, 1.2f), Tree(Pine, 1.6f, 21f, 28.8f), Tree(Common, 1.2f, 9f, 1.2f),
             .. Prop(Log1, 28f, 22.5f, 90), TreeWall(0.1f, 3.6f, 7.1f, 12.7f), TreeWall(0.1f, 3.6f, 17.3f, 29.9f),
             TreeWall(26.4f, 29.9f, 0.1f, 12.7f), TreeWall(26.4f, 29.9f, 25.3f, 29.9f)],
            [Pack(15, 8), Pack(15, 22), Pack(11, 15), Pack(19, 15), Rare(15, 15)], ["path", "forest", "ruins"])
            { DoodadSlots = [LargeDoodad(3, 3), Doodad(7.7f, 26.1f), Doodad(21.8f, 3.9f)], Hints = new(TreeMix.Old) },
        new("forest_hollow_01", [Side.S],
            [Drum(5, 10, 2), Drum(5, 20, 2), Drum(10, 26, 2), Drum(20, 26, 2), Drum(25, 20, 2), Drum(25, 10, 2), .. Prop(Log2, 15f, 28f, 0),
             TreeWall(22.3f, 29.9f, 26.4f, 29.9f), TreeWall(0.1f, 3.6f, 0.1f, 8.1f), TreeWall(0.1f, 2.6f, 8.1f, 11.9f),
             TreeWall(0.1f, 3.6f, 11.9f, 18.1f), TreeWall(0.1f, 2.6f, 18.1f, 24.4f), TreeWall(26.4f, 29.9f, 0.1f, 8.1f),
             TreeWall(27.4f, 29.9f, 8.1f, 11.9f), TreeWall(26.4f, 29.9f, 11.9f, 18.1f), TreeWall(27.4f, 29.9f, 18.1f, 26.4f)],
            [Pack(10, 15), Pack(20, 15), Pack(15, 21), Pack(15, 9), Rare(15, 15)], ["deadend", "forest", "thicket"])
            { DoodadSlots = [LargeDoodad(8, 3), Doodad(22, 3), Doodad(3, 27)], Hints = new(TreeMix.Pine) },
        new("forest_glade_01", [Side.N, Side.E],
            [Rocks(2, 9, 2, 9), Drum(6, 24, 2.5f), Drum(24, 6, 2.5f), Drum(24, 24, 2), Tree(Pine, 1.2f, 28.8f, 29f),
             Tree(Pine, 1.2f, 28.8f, 21.5f), Tree(Common, 1.3f, 9f, 28.8f), .. Prop(Log1, 15f, 7f, 0),
             .. Prop("avalon_rock_leaning_1", 21.6f, 28.2f, 0), TreeWall(9.3f, 12.9f, 0.1f, 3.6f), TreeWall(18.1f, 29.9f, 0.1f, 3.1f),
             TreeWall(0.1f, 3.6f, 9.3f, 22.5f), TreeWall(0.1f, 3.1f, 22.5f, 29.9f)],
            [Pack(15, 15), Pack(11, 18), Pack(18, 11), Pack(20, 20)], ["path", "forest", "glade"])
            { DoodadSlots = [LargeDoodad(7.7f, 15.5f), Doodad(15.5f, 3), Doodad(10.5f, 12.5f)], Hints = new(TreeMix.Broadleaf) },
        // Forest dressing (#1109): fifteen more, three of each exit shape: dead ends, straights, corners, T junctions
        // and crosses.
        new("forest_hollow_02", [Side.S],
            [Drum(7, 9, 2), Drum(5, 21, 2.5f), Drum(15, 26, 2), Rocks(21, 27, 20, 27), Drum(25, 12, 2), .. Prop(Log2, 11, 26, 90),
             TreeWall(5.6f, 10.2f, 26.4f, 29.9f), TreeWall(17.3f, 20.6f, 26.4f, 29.9f), TreeWall(20.6f, 27.3f, 27.4f, 29.9f),
             TreeWall(27.3f, 29.9f, 26.4f, 29.9f), TreeWall(0.1f, 3.6f, 0.1f, 18.5f), TreeWall(0.1f, 2.1f, 18.5f, 24.6f),
             TreeWall(26.4f, 29.9f, 0.1f, 10.1f), TreeWall(27.4f, 29.9f, 10.1f, 19.6f), TreeWall(27.4f, 29.9f, 19.6f, 26.4f)],
            [Pack(10, 15), Pack(20, 14), Pack(15, 21), Rare(15, 11)], ["deadend", "forest", "hollow"])
            { DoodadSlots = [LargeDoodad(22.3f, 4), Doodad(6.2f, 4.5f), Doodad(3, 27)], Hints = new(TreeMix.Pine, MeetX: 15, MeetZ: 17) },
        new("forest_ruin_03", [Side.W],
            [Wall(18, 26, 24, 26), Wall(25, 27, 16, 22), Wall(18, 24, 4, 6), Pillar(27, 11, 1.5f), Pillar(9, 24, 1.5f), Pillar(9, 6, 1.5f),
             Tree(Common, 1.1f, 1.2f, 22), Tree(Pine, 1.4f, 1.2f, 7.5f), TreeWall(0.1f, 29.9f, 0.1f, 3.6f),
             TreeWall(8.1f, 29.9f, 26.4f, 29.9f), TreeWall(26.4f, 29.9f, 3.6f, 9.2f), TreeWall(26.4f, 29.9f, 12.8f, 15.7f),
             TreeWall(27.4f, 29.9f, 15.7f, 22.3f), TreeWall(26.4f, 29.9f, 22.3f, 26.4f)],
            [Pack(15, 15), Pack(21, 15), Pack(15, 20), Pack(15, 10), Rare(21, 19)], ["deadend", "forest", "ruins"])
            { DoodadSlots = [LargeDoodad(4, 27), Doodad(4.2f, 6.2f), Doodad(23.7f, 8.6f)], Hints = new(TreeMix.Old) },
        new("forest_mire_01", [Side.E],
            [.. Prop(Log1, 7, 21, 90), .. Prop(Log2, 20, 6, 0), Stones(6, 8, 1.5f), Stones(21, 26, 1.5f), Drum(4, 14, 2),
             Tree(Dead, 1.2f, 12, 25), Tree(Dead, 1.1f, 13, 4.5f), Tree(Dead, 1.1f, 28.8f, 22), TreeWall(5.6f, 12.2f, 0.1f, 3.6f),
             TreeWall(13.8f, 29.9f, 0.1f, 3.6f), TreeWall(5.6f, 19.2f, 26.4f, 29.9f), TreeWall(19.2f, 23, 27.9f, 29.9f),
             TreeWall(0.1f, 3.6f, 5.6f, 11.7f), TreeWall(0.1f, 3.6f, 16.3f, 24.4f)],
            [Pack(15, 15), Pack(10, 11), Pack(11, 20), Rare(20, 21)], ["deadend", "forest", "mire"])
            { DoodadSlots = [LargeDoodad(27, 27), Doodad(3, 3), Doodad(3, 27)], Hints = new(TreeMix.Old) },
        // The rock arch over the way through: its legs stand either side of the meeting point, 4.3 m apart, where both
        // paths run under its span (its underside 2.95 m up, the agent 2 m tall); only the legs are blockers. Tagged
        // overhang: capped at one a layout in chunk-pools.json.
        new("forest_rocks_03", [Side.N, Side.S],
            [.. Prop("avalon_rock_arch", 15, 15, 0), Rocks(3, 9, 19, 26), Rocks(20, 27, 6, 11), Stones(24, 22, 1.5f), Stones(7, 8, 1.5f),
             Tree(Pine, 1.2f, 6, 28.8f), Tree(Pine, 1.2f, 24, 1.2f), TreeWall(0.1f, 3.6f, 0.1f, 18.6f), TreeWall(0.1f, 2.6f, 18.6f, 26.3f),
             TreeWall(0.1f, 3.6f, 26.3f, 29.9f), TreeWall(27.4f, 29.9f, 0.1f, 11.3f), TreeWall(26.4f, 29.9f, 11.3f, 29.9f)],
            [Pack(15, 8), Pack(15, 22), Pack(7, 13), Pack(23, 17)], ["path", "forest", "rocks", "overhang"])
            { DoodadSlots = [LargeDoodad(7.7f, 2.4f), Doodad(23.8f, 27), Doodad(21.7f, 3.4f)] },
        new("forest_grove_01", [Side.E, Side.W],
            [Drum(6, 5, 2.5f), Drum(24, 5, 2), Drum(25.5f, 23, 2.5f), Drum(15, 25, 2), Tree(Common, 1.2f, 1.2f, 21),
             Tree(Common, 1.2f, 28.8f, 8), TreeWall(0.1f, 8.5f, 0.1f, 2.1f), TreeWall(8.5f, 22.1f, 0.1f, 3.6f),
             TreeWall(22.1f, 29.9f, 0.1f, 2.6f), TreeWall(0.1f, 13.1f, 26.4f, 29.9f), TreeWall(13.1f, 16.9f, 27.4f, 29.9f),
             TreeWall(16.9f, 29.9f, 26.4f, 29.9f)],
            [Pack(10, 15), Pack(20, 15), Pack(12, 19), Rare(15, 9)], ["path", "forest", "grove"])
            { DoodadSlots = [LargeDoodad(6, 22.3f), Doodad(20.5f, 23.8f), Doodad(13.7f, 6.2f)], Hints = new(TreeMix.Broadleaf) },
        new("forest_thicket_03", [Side.N, Side.S],
            [Drum(7, 7, 2.5f), Drum(24, 10, 2.5f), Drum(5, 19, 2), Drum(22.5f, 24, 2), Tree(Pine, 1.2f, 8.5f, 28.8f),
             Tree(Pine, 1.3f, 21.5f, 1.2f), .. Prop(Log2, 24, 17, 90), TreeWall(0.1f, 3.6f, 0.1f, 17.1f),
             TreeWall(0.1f, 2.6f, 17.1f, 20.9f), TreeWall(0.1f, 3.6f, 20.9f, 29.9f), TreeWall(26.4f, 29.9f, 0.1f, 8.5f),
             TreeWall(26.9f, 29.9f, 8.5f, 11.5f), TreeWall(26.4f, 29.9f, 11.5f, 29.9f)],
            [Pack(10, 15), Pack(20, 15), Pack(15, 9), Pack(15, 21)], ["path", "forest", "thicket"])
            { DoodadSlots = [LargeDoodad(8.3f, 24.1f), Doodad(20.3f, 28), Doodad(6.2f, 2)], Hints = new(TreeMix.Pine) },
        // A rock overhang in the corner away from both exits, its ledge (its underside at least 2.7 m up) reaching west
        // over open floor, side-on to the three-quarter camera (pointed at the camera, a ledge reads as a cap on a
        // stem); only its base is a blocker. Tagged overhang: capped at one a layout in chunk-pools.json.
        new("forest_rocks_04", [Side.S, Side.W],
            [.. Prop("avalon_rock_overhang", 22.5f, 22.5f, 270), Rocks(3, 9, 3, 9), Stones(25, 9, 1.5f), Drum(11, 26.5f, 2),
             Tree(Pine, 1.4f, 22, 1.2f), Tree(Pine, 1.2f, 1.2f, 22), TreeWall(0.1f, 8.7f, 26.4f, 29.9f),
             TreeWall(13.3f, 20.5f, 26.4f, 29.9f), TreeWall(20.5f, 24.9f, 27.4f, 29.9f), TreeWall(26.4f, 29.9f, 0.1f, 7.8f),
             TreeWall(26.4f, 29.9f, 10.2f, 24.9f)],
            [Pack(15, 15), Pack(19, 18), Pack(10, 21), Pack(20, 11), Rare(13, 8)], ["path", "forest", "rocks", "overhang"])
            { DoodadSlots = [LargeDoodad(5.9f, 22.3f), Doodad(23.8f, 4), Doodad(27.5f, 27.5f)] },
        new("forest_glade_02", [Side.N, Side.W],
            [Rocks(19, 26, 3, 9), Drum(24, 24, 2.5f), Drum(7, 8, 2), Stones(22, 15, 1.5f), Tree(Common, 1.1f, 9, 28.8f),
             Tree(Common, 1.2f, 1.2f, 8), Tree(Pine, 1.3f, 1.2f, 22), .. Prop(Log1, 11, 4, 90), TreeWall(5.6f, 10.1f, 0.1f, 3.6f),
             TreeWall(11.9f, 18.6f, 0.1f, 3.6f), TreeWall(18.6f, 26.3f, 0.1f, 2.6f), TreeWall(26.3f, 29.9f, 0.1f, 3.6f),
             TreeWall(26.4f, 29.9f, 3.6f, 22.5f), TreeWall(26.9f, 29.9f, 22.5f, 29.9f)],
            [Pack(15, 15), Pack(10, 20), Pack(20, 20), Pack(16, 10)], ["path", "forest", "glade"])
            { DoodadSlots = [LargeDoodad(4, 26), Doodad(23.8f, 18.7f), Doodad(3, 3)], Hints = new(TreeMix.Broadleaf) },
        new("forest_mire_02", [Side.E, Side.S],
            [Stones(7, 22, 1.5f), .. Prop(Log1, 6, 8, 0), .. Prop(Log2, 22, 25, 0), Drum(24, 6, 2), Tree(Dead, 1.3f, 11, 24),
             Tree(Dead, 1.2f, 28.8f, 21), Tree(Pine, 1.3f, 28.8f, 1.2f), TreeWall(13.5f, 29.9f, 26.4f, 29.9f),
             TreeWall(0.1f, 3.6f, 0.1f, 7.1f), TreeWall(0.1f, 3.6f, 8.9f, 20), TreeWall(0.1f, 3.1f, 20, 23)],
            [Pack(15, 15), Pack(10, 12), Pack(11, 19), Rare(20, 20)], ["path", "forest", "mire"])
            { DoodadSlots = [LargeDoodad(4, 27), Doodad(26.7f, 23.8f), Doodad(6.2f, 3)], Hints = new(TreeMix.Old) },
        new("forest_ruin_04", [Side.E, Side.S, Side.W],
            [Wall(6, 13, 21, 23), Wall(17, 24, 21, 23), Pillar(6, 6, 1.5f), Pillar(24, 6, 1.5f), Tree(Pine, 1.2f, 1.2f, 22),
             TreeWall(0.1f, 9.9f, 0.1f, 3.6f), TreeWall(20.1f, 24.4f, 0.1f, 2.1f), TreeWall(5.6f, 12.8f, 26.4f, 29.9f),
             TreeWall(12.8f, 17.2f, 27.9f, 29.9f), TreeWall(17.2f, 29.9f, 26.4f, 29.9f), TreeWall(0.1f, 3.6f, 3.6f, 9.9f),
             TreeWall(27.9f, 29.9f, 6.1f, 9.9f)],
            [Pack(15, 15), Pack(10, 18), Pack(20, 18), Rare(15, 25), Pack(9, 10), Pack(21, 10)], ["junction", "forest", "ruins"])
            { DoodadSlots = [LargeDoodad(28, 20.3f), Doodad(3, 27), Doodad(27, 2.5f)], Hints = new(TreeMix.Old) },
        new("forest_grove_02", [Side.N, Side.S, Side.W],
            [Drum(24, 15, 2.5f), Drum(6, 6, 2.5f), Drum(6, 24, 2.5f), Tree(Pine, 1.2f, 21.5f, 28.8f), Tree(Pine, 1.2f, 21.5f, 1.2f),
             TreeWall(0.1f, 9.9f, 0.1f, 3.1f), TreeWall(0.1f, 9.9f, 26.9f, 29.9f), TreeWall(0.1f, 3.1f, 3.1f, 9.9f),
             TreeWall(0.1f, 3.1f, 20.1f, 26.9f), TreeWall(26.4f, 29.9f, 0.1f, 13.5f), TreeWall(26.9f, 29.9f, 13.5f, 16.5f),
             TreeWall(26.4f, 29.9f, 16.5f, 29.9f)],
            [Pack(15, 15), Pack(10, 10), Pack(10, 20), Pack(19, 10), Rare(19, 21)], ["junction", "forest", "grove"])
            { DoodadSlots = [LargeDoodad(22.3f, 5.8f), Doodad(23.8f, 26), Doodad(23.8f, 20.5f)], Hints = new(TreeMix.Broadleaf) },
        new("forest_thicket_04", [Side.N, Side.E, Side.W],
            [Drum(7, 23, 2), Drum(23, 23, 2), Drum(5, 5, 2), Drum(25, 5, 2), Tree(Pine, 1.2f, 1.2f, 8), TreeWall(0.1f, 6.9f, 0.1f, 2.6f),
             TreeWall(6.9f, 23.1f, 0.1f, 3.6f), TreeWall(23.1f, 29.9f, 0.1f, 2.6f), TreeWall(5.6f, 9.9f, 27.9f, 29.9f),
             TreeWall(20.1f, 22.9f, 27.4f, 29.9f), TreeWall(0.1f, 2.6f, 20.1f, 24.4f), TreeWall(27.4f, 29.9f, 20.1f, 22.9f)],
            [Pack(15, 15), Pack(9, 10), Pack(21, 10), Pack(10, 19), Pack(20, 19)], ["junction", "forest", "thicket"])
            { DoodadSlots = [LargeDoodad(15, 7.7f), Doodad(27, 27), Doodad(3, 27)], Hints = new(TreeMix.Pine) },
        new("forest_glade_03", [Side.N, Side.E, Side.S, Side.W],
            [Drum(7, 7, 2), Rocks(21, 26, 21, 26), .. Prop(Log2, 28, 23.5f, 90), TreeWall(5.6f, 9.9f, 0.1f, 2.6f),
             TreeWall(20.1f, 29.9f, 0.1f, 3.6f), TreeWall(0.1f, 9.9f, 26.4f, 29.9f), TreeWall(20.1f, 29.9f, 26.4f, 29.9f),
             TreeWall(0.1f, 2.6f, 5.6f, 9.9f), TreeWall(0.1f, 3.6f, 20.1f, 26.4f), TreeWall(26.4f, 29.9f, 3.6f, 9.9f)],
            [Pack(15, 15), Pack(11, 11), Pack(19, 19), Rare(9, 21)], ["junction", "forest", "glade"])
            { DoodadSlots = [LargeDoodad(22.3f, 7.7f), Doodad(3, 3), Doodad(6.2f, 23.8f)], Hints = new(TreeMix.Broadleaf) },
        new("forest_ruin_05", [Side.N, Side.E, Side.S, Side.W],
            [Pillar(9, 9, 1.5f), Pillar(21, 9, 1.5f), Pillar(9, 21, 1.5f), Pillar(21, 21, 1.5f), Wall(2, 8, 25, 27), Wall(25, 27, 2, 8),
             Tree(Pine, 1.2f, 1.2f, 8.5f), Tree(Pine, 1.2f, 8.5f, 1.2f), Tree(Common, 1.2f, 28.8f, 28.8f),
             TreeWall(20.1f, 24.6f, 0.1f, 3.6f), TreeWall(27.3f, 29.9f, 0.1f, 3.6f), TreeWall(1.6f, 8.3f, 27.4f, 29.9f),
             TreeWall(20.1f, 23.4f, 26.4f, 29.9f), TreeWall(27.4f, 29.9f, 3.6f, 9.9f), TreeWall(26.4f, 29.9f, 20.1f, 23.4f)],
            [Pack(15, 15), Pack(15, 9), Pack(9, 15), Pack(21, 15), Rare(15, 21)], ["junction", "forest", "ruins"])
            { DoodadSlots = [LargeDoodad(4, 4), Doodad(26, 26), Doodad(4, 21)], Hints = new(TreeMix.Old) },
        new("forest_grove_03", [Side.N, Side.E, Side.S, Side.W],
            [Drum(7.5f, 6, 2), Drum(22.5f, 6, 2), Drum(7, 23, 2.5f), TreeWall(0.1f, 9.9f, 0.1f, 3.6f), TreeWall(20.1f, 29.9f, 0.1f, 3.6f),
             TreeWall(0.1f, 9.9f, 26.4f, 29.9f), TreeWall(20.1f, 29.9f, 26.4f, 29.9f), TreeWall(0.1f, 3.6f, 3.6f, 9.9f),
             TreeWall(0.1f, 3.6f, 20.1f, 26.4f), TreeWall(26.4f, 29.9f, 3.6f, 9.9f), TreeWall(26.4f, 29.9f, 20.1f, 26.4f)],
            [Pack(15, 15), Pack(11, 11), Pack(19, 11), Pack(11, 19), Rare(19, 19)], ["junction", "forest", "grove"])
            { DoodadSlots = [LargeDoodad(22.3f, 22.3f), Doodad(6.1f, 10.5f), Doodad(23.9f, 10.5f)] },
    ];

    public static IReadOnlyList<ChunkGroupPiece> Groups() =>
    [
        new("forest_clearing_big", [(0, 0, Side.S), (1, 0, Side.E), (1, 1, Side.N), (0, 1, Side.W)],
            [Drum(6, 6, 3), Drum(54, 6, 3), Drum(6, 54, 3), Drum(54, 54, 3), Tree(Pine, 1.3f, 58.8f, 29f), Tree(Common, 1.2f, 31.5f, 58.8f),
             Tree(Common, 1.2f, 1.8f, 39f), Tree(Common, 1.4f, 58.2f, 21f), Tree(Pine, 1.1f, 38.5f, 58.8f), Tree(Pine, 1.3f, 1.2f, 31.5f),
             Tree(Pine, 1.5f, 21f, 1.2f), Tree(Pine, 1.3f, 29f, 1.2f), Tree(Common, 1.2f, 51f, 58.8f), Tree(Common, 1.2f, 8.5f, 1.2f),
             Tree(Common, 1.3f, 58.8f, 8f), Tree(Common, 1.3f, 1.8f, 51f), .. Prop(Log2, 35f, 6.5f, 0), .. Prop(Log2, 7f, 26.5f, 0),
             .. Prop(Log1, 25f, 7f, 90), TreeWall(35.6f, 51.7f, 0.1f, 3.6f), TreeWall(51.7f, 59.9f, 0.1f, 2.6f),
             TreeWall(0.1f, 8.3f, 57.4f, 59.9f), TreeWall(8.3f, 29.9f, 56.4f, 59.9f), TreeWall(0.1f, 2.6f, 0.1f, 8.3f),
             TreeWall(0.1f, 3.6f, 8.3f, 29.9f), TreeWall(56.4f, 59.9f, 30.1f, 51.7f), TreeWall(57.4f, 59.9f, 51.7f, 59.9f)],
            [Pack(14, 22), Pack(22, 14), Pack(46, 22), Pack(38, 14), Pack(14, 38), Pack(22, 46), Pack(46, 38), Pack(38, 46),
             Leader(40, 20), Leader(20, 40)],
            ["forest", "setpiece", "clearing"])
        {
            DoodadSlots = [SetDoodad(6.2f, 17.5f), SetDoodad(33, 3), SetDoodad(15.5f, 53.8f), SetDoodad(53.8f, 44.5f)],
            Hints = new(TreeMix.Broadleaf),
        },
        new("forest_grove_ruin", [(0, 0, Side.S), (1, 0, Side.E), (1, 1, Side.N)],
            [Pillar(20, 20, 1.5f), Pillar(40, 20, 1.5f), Pillar(20, 40, 1.5f), Pillar(40, 40, 1.5f), Wall(4, 12, 50, 54),
             Wall(48, 52, 3, 9), Tree(Pine, 1.2f, 31f, 58.8f), Tree(Dead, 1.1f, 58.8f, 28.5f), Tree(Pine, 1.4f, 29f, 1.2f),
             Tree(Common, 1.2f, 51.5f, 58.8f), Tree(Common, 1.1f, 21f, 1.2f), Tree(Common, 1.2f, 9f, 1.8f), Tree(Pine, 1.4f, 39f, 57.5f),
             Tree(Pine, 1.4f, 58.2f, 21f), Tree(Pine, 1.3f, 58.8f, 9f), .. Prop(Log1, 6.5f, 34f, 90), .. Prop(Log2, 7f, 21.5f, 90),
             .. Prop(Log1, 6.5f, 11f, 90), TreeWall(30.1f, 47.6f, 0.1f, 3.6f), TreeWall(47.6f, 52.3f, 0.1f, 2.6f),
             TreeWall(52.3f, 59.9f, 0.1f, 3.6f), TreeWall(0.1f, 29.9f, 56.4f, 59.9f), TreeWall(0.1f, 3.6f, 0.1f, 29.9f),
             TreeWall(0.1f, 3.6f, 30.1f, 56.4f), TreeWall(56.4f, 59.9f, 30.1f, 59.9f)],
            [Pack(10, 12), Pack(24, 10), Pack(36, 10), Pack(50, 24), Pack(10, 46), Pack(24, 52), Pack(36, 52), Pack(50, 48),
             Leader(45, 40), Leader(15, 40), Rare(45, 15)],
            ["forest", "setpiece", "ruins"])
        {
            DoodadSlots = [SetDoodad(6.2f, 3.8f), SetDoodad(33, 16.5f), SetDoodad(9.7f, 33), SetDoodad(53.8f, 56.2f)],
            Hints = new(TreeMix.Old),
        },
        new("forest_arena", [(0, 0, Side.S)],
            [Stones(16, 16, 1.5f), Stones(44, 16, 1.5f), Stones(16, 44, 1.5f), Stones(44, 44, 1.5f), Rocks(2, 8, 52, 58),
             Rocks(52, 58, 52, 58), Rocks(52, 58, 2, 8), Tree(Pine, 1.2f, 28f, 1.2f), Tree(Pine, 1.5f, 9f, 1.2f),
             Tree(Pine, 1.6f, 21f, 1.2f), .. Prop(Log1, 53f, 23.5f, 90), .. Prop(Log2, 6.5f, 13.5f, 90), .. Prop(Log2, 7f, 23f, 90),
             TreeWall(30.1f, 51.6f, 0.1f, 3.6f), TreeWall(8.3f, 29.9f, 56.4f, 59.9f), TreeWall(30.1f, 51.6f, 56.4f, 59.9f),
             TreeWall(0.1f, 3.6f, 0.1f, 29.9f), TreeWall(0.1f, 3.6f, 30.1f, 51.6f), TreeWall(56.4f, 59.9f, 8.3f, 29.9f),
             TreeWall(56.4f, 59.9f, 30.1f, 51.6f)],
            [Boss(36, 36), Leader(38, 22), Leader(22, 38),
             Pack(15, 10), Pack(24, 22), Pack(48, 10), Pack(40, 8), Pack(10, 48), Pack(8, 40)],
            ["forest", "setpiece", "boss", "arena"])
        {
            DoodadSlots = [SetLargeDoodad(7.7f, 5.8f), SetDoodad(6.2f, 18.1f), SetLargeDoodad(47.5f, 27), SetDoodad(33, 14),
                SetLargeDoodad(18, 52.3f), SetDoodad(14, 33), SetLargeDoodad(33, 47.5f), SetDoodad(40.5f, 53.8f)],
        },
    ];
}
