namespace Avalon.ChunkGen;

/// <summary>
/// Glimmerdell (map 1) built from the medieval village kit (design of 2026-10-17): four 30 m squares, world X/Z 0-60,
/// the arrival point at (15, 15) south-west, the forest portal at (15, 45) north-west, the market south-east and the
/// bank and inn north-east, with the inner walls at X = 30 and Z = 30 opened at 12-18 and 42-48. Each building is a
/// recipe (Recipes/*.json) placed with <see cref="TownBuilding.Place" />, its front on the -X or -Z side the camera sees;
/// each prop is a kit or avalon_* model with a collision box around it; the walls and the paving are decor over today's
/// wall and floor boxes. Every solid's top is at least 1.05 m up (owner decision 8): the navmesh climbs any step of
/// 1.0 m or less. Every coordinate is chunk-local.
/// Edit this file and rerun "dotnet run --project tools/Avalon.ChunkGen -- town"; TownPiecesShould checks the rules and
/// the committed files, TownNpcPlacementShould the NPC spots against it.
/// </summary>
public static class TownPieces
{
    public const float ArrivalX = 15f;
    public const float ArrivalZ = 15f;
    public const float PortalX = 15f;
    public const float PortalZ = 45f;

    /// <summary>A solid prop's collision never tops out lower than this (owner decision 8).</summary>
    private const float MinSolidTop = 1.05f;

    private static WallSegment Wall(string name, float minX, float maxX, float minZ, float maxZ) => new(name, minX, maxX, minZ, maxZ);

    // Today's wall boxes (0.5 m thick, centred on the cell edge; the inner walls open at 12-18).
    private static readonly WallSegment s_wallN = Wall("Wall_N", 0, 30, 29.75f, 30.25f);
    private static readonly WallSegment s_wallNL = Wall("Wall_N_L", 0, 12, 29.75f, 30.25f);
    private static readonly WallSegment s_wallNR = Wall("Wall_N_R", 18, 30, 29.75f, 30.25f);
    private static readonly WallSegment s_wallS = Wall("Wall_S", 0, 30, -0.25f, 0.25f);
    private static readonly WallSegment s_wallSL = Wall("Wall_S_L", 0, 12, -0.25f, 0.25f);
    private static readonly WallSegment s_wallSR = Wall("Wall_S_R", 18, 30, -0.25f, 0.25f);
    private static readonly WallSegment s_wallE = Wall("Wall_E", 29.75f, 30.25f, 0, 30);
    private static readonly WallSegment s_wallEL = Wall("Wall_E_L", 29.75f, 30.25f, 0, 12);
    private static readonly WallSegment s_wallER = Wall("Wall_E_R", 29.75f, 30.25f, 18, 30);
    private static readonly WallSegment s_wallW = Wall("Wall_W", -0.25f, 0.25f, 0, 30);
    private static readonly WallSegment s_wallWL = Wall("Wall_W_L", -0.25f, 0.25f, 0, 12);
    private static readonly WallSegment s_wallWR = Wall("Wall_W_R", -0.25f, 0.25f, 18, 30);

    public static IReadOnlyList<TownSquare> Squares() =>
    [
        Square("town_sw_01", 0, 0, [Side.N, Side.E], [s_wallNL, s_wallNR, s_wallEL, s_wallER, s_wallS, s_wallW], Arrival, isEntry: true, hasForwardPortal: false, ["town", "entry"]),
        Square("town_se_01", 1, 0, [Side.N, Side.W], [s_wallNL, s_wallNR, s_wallE, s_wallS, s_wallWL, s_wallWR], Market, isEntry: false, hasForwardPortal: false, ["town"]),
        Square("town_nw_01", 0, 1, [Side.E, Side.S], [s_wallN, s_wallEL, s_wallER, s_wallSL, s_wallSR, s_wallW], Portal, isEntry: false, hasForwardPortal: true, ["town"]),
        Square("town_ne_01", 1, 1, [Side.S, Side.W], [s_wallN, s_wallE, s_wallSL, s_wallSR, s_wallWL, s_wallWR], BankAndInn, isEntry: false, hasForwardPortal: false, ["town"]),
    ];

    private static TownSquare Square(
        string name, int gridX, int gridZ, IReadOnlyList<Side> exits, IReadOnlyList<WallSegment> walls, Action<SquareBuilder> build,
        bool isEntry, bool hasForwardPortal, IReadOnlyList<string> tags)
    {
        var square = new SquareBuilder();
        build(square);
        square.Pave(exits);
        square.DrawWalls(walls);
        return new TownSquare(name, gridX, gridZ, exits, walls, square.Pieces, isEntry, hasForwardPortal, tags)
        {
            Decor = square.Decor,
            WallsAsDecor = true,
            Paved = true,
        };
    }

    /// <summary>South-west: the town hall, the fountain with its benches, lamp posts, the notice board, a house.</summary>
    private static void Arrival(SquareBuilder s)
    {
        // In the square's north-east corner, turned so its door and balcony face west (the camera's side) and Uriel
        // stands with only the low plaza south-west of him; clear of the north and east doorway lanes, the walls and
        // the ground around the arrival point the players gather on.
        s.Building("town_hall", "Town hall", 22.8f, 22.85f, 1);
        s.Fountain(15f, 7.5f);
        s.Prop("avalon_bench", "Benches", "bench W", 10.25f, 7.5f, 90);
        s.Prop("avalon_bench", "Benches", "bench E", 19.75f, 7.5f, 270);
        s.Prop("avalon_bench", "Benches", "bench S", 15f, 3.65f, 0);
        s.Prop("avalon_lamp_post", "Lamp posts", "lamp post 1", 10.5f, 20.5f, 0);
        s.Prop("avalon_lamp_post", "Lamp posts", "lamp post 2", 10.5f, 10f, 0);
        s.Prop("avalon_lamp_post", "Lamp posts", "lamp post 3", 20.5f, 12.5f, 0);
        s.Prop("avalon_lamp_post", "Lamp posts", "lamp post 4", 25.5f, 12.5f, 0);
        s.Prop("avalon_notice_board", "Notice board", "board", 9f, 24f, 90);
        // A house in the south-east corner, south of the east doorway lane: south-west of every NPC is kept low, so
        // the camera sees each one over it.
        s.Building("house_a", "House A", 23.5f, 7.1f, 0);
    }

    /// <summary>North-west: the gate on the road to the portal, the watchtower, the hunter's lodge, a cart, crates.</summary>
    private static void Portal(SquareBuilder s)
    {
        // The road from the arrival point runs north through the gate (its lintel 3.12 m up over X 12-18) to the portal.
        s.Building("gate_arch", "Gate arch", 15f, 12f, 0);
        s.Building("watchtower", "Watchtower", 6f, 24f, 0);
        // Facing west, its awning over the door; south of the east doorway lane and north of the south one.
        s.Building("hunters_lodge", "Hunter's lodge", 22.7f, 7.2f, 0);
        s.Prop("Prop_Wagon", "Cart", "wagon", 5f, 17f, 0);
        s.Prop("Prop_Crate", "Crates (lodge)", "crate 1", 8.5f, 15.5f, 0);
        s.Prop("Prop_Crate", "Crates (lodge)", "crate 2", 9.7f, 15.5f, 90);
        s.Prop("avalon_lamp_post", "Lamp posts", "lamp post 1", 11f, 18f, 0);
        s.Prop("avalon_lamp_post", "Lamp posts", "lamp post 2", 19f, 18f, 0);
        s.Prop("avalon_lamp_post", "Lamp posts", "lamp post 3", 11f, 5f, 0);
        s.Prop("avalon_bench", "Benches", "bench lodge", 23f, 14f, 0);
    }

    /// <summary>South-east: the smithy with its lean-to and forge tools, the armourer's stall, the general-goods stall.</summary>
    /// <remarks>
    /// The walking benchmarks (tests/Avalon.World.Testing, TownNavmesh.LoopCentre) walk an 8 m loop about world (45, 10),
    /// this square's (15, 10): nothing solid stands 7-9 m from it, a metre clear either side of the walkers.
    /// </remarks>
    private static void Market(SquareBuilder s)
    {
        // In the square's north-east corner, its lean-to to the south (the camera's side), its tools at its sides.
        s.Building("smithy", "Smithy", 22.5f, 22.8f, 0);
        s.Prop("avalon_anvil", "Smithy", "anvil", 25.3f, 17.8f, 0);
        s.Prop("avalon_quench_trough", "Smithy", "quench trough", 17f, 21.5f, 90);
        s.Prop("avalon_wood_pile", "Smithy", "wood pile", 17f, 24.5f, 90);
        s.Prop("avalon_barrel", "Barrels (smithy)", "barrel 1", 14.5f, 22f, 0);
        s.Prop("avalon_barrel", "Barrels (smithy)", "barrel 2", 14.5f, 23f, 0);
        // Counters to the west and to the south, each merchant in front of it on the camera's side.
        s.Prop("avalon_stall_armourer", "Armourer's stall", "stall", 25.5f, 15f, 270, front: Facing.NegX);
        s.Prop("Prop_Crate", "Crates (armourer)", "crate 1", 26f, 5f, 0);
        s.Prop("Prop_Crate", "Crates (armourer)", "crate 2", 26f, 3.8f, 90);
        s.Prop("avalon_stall_goods", "General-goods stall", "stall", 7f, 24f, 180, front: Facing.NegZ);
        s.Prop("Prop_Crate", "Crates (general goods)", "crate 1", 10.5f, 25f, 0);
        s.Prop("avalon_barrel", "Barrels (general goods)", "barrel", 10.5f, 23.3f, 0);
        s.Prop("avalon_bench", "Benches", "bench market", 5f, 10f, 90);
        s.Prop("avalon_lamp_post", "Lamp posts", "lamp post 1", 5.5f, 19f, 0);
        s.Prop("avalon_lamp_post", "Lamp posts", "lamp post 2", 14f, 20.5f, 0);
    }

    /// <summary>North-east: the bank, the inn, a house, the well.</summary>
    private static void BankAndInn(SquareBuilder s)
    {
        s.Building("bank", "Bank", 8f, 21.5f, 0);
        s.Building("inn", "Inn", 21.5f, 20.5f, 0);
        s.Building("house_b", "House B", 24.5f, 6.5f, 0);
        s.Prop("avalon_well", "Well", "well", 15f, 13.5f, 0);
        s.Prop("avalon_bench", "Benches", "bench well", 18f, 10f, 270);
        s.Prop("Prop_Crate", "Crates (house B)", "crate 1", 19.5f, 7.2f, 0);
        s.Prop("avalon_barrel", "Barrels (house B)", "barrel 1", 19.5f, 4.5f, 0);
        s.Prop("avalon_barrel", "Barrels (house B)", "barrel 2", 19.5f, 5.5f, 0);
        s.Prop("avalon_lamp_post", "Lamp posts", "lamp post 1", 11.5f, 16.5f, 0);
        s.Prop("avalon_lamp_post", "Lamp posts", "lamp post 2", 18.5f, 3.5f, 0);
    }

    /// <summary>Collects one square's collision pieces and its decor.</summary>
    private sealed class SquareBuilder
    {
        private const float Tile = 2f;

        public List<TownPiece> Pieces { get; } = [];

        public List<DecorItem> Decor { get; } = [];

        public void Building(string recipe, string name, float x, float z, int rotation)
        {
            (IReadOnlyList<TownPiece> proxies, IReadOnlyList<DecorItem> decor) = TownBuilding.Place(BuildingRecipe.Load(recipe), name, x, z, rotation);
            Pieces.AddRange(proxies);
            Decor.AddRange(decor);
        }

        /// <summary>A model with one collision box around its turned bounds, standing at least <see cref="MinSolidTop" />.</summary>
        public void Prop(string model, string building, string part, float x, float z, int yaw, Facing front = Facing.None)
        {
            var item = new DecorItem(building, model, x, 0f, z, yaw);
            (float minX, float maxX, _, float maxY, float minZ, float maxZ) = DecorRules.WorldBounds(item);
            Decor.Add(item);
            Pieces.Add(new BoxPiece(building, part, Material.Collision, minX, maxX, minZ, maxZ, 0f, Math.Max(maxY, MinSolidTop)) { Front = front });
        }

        /// <summary>The fountain keeps its shapes: a rim the water shows inside, the column and the upper bowl.</summary>
        public void Fountain(float x, float z)
        {
            Decor.Add(new DecorItem("Fountain", "avalon_fountain", x, 0f, z, 0));
            Pieces.Add(new RingPiece("Fountain", "basin", Material.Collision, x, z, 2.2f, 1.8f, 0f, 1.1f));   // a 0.4 m rim, 1.1 m up (owner decision 1)
            Pieces.Add(new CylinderPiece("Fountain", "water", Material.Collision, x, z, 1.78f, 0.9f, 1f));
            Pieces.Add(new CylinderPiece("Fountain", "column", Material.Collision, x, z, 0.5f, 0f, 2.2f));
            Pieces.Add(new CylinderPiece("Fountain", "upper bowl", Material.Collision, x, z, 0.9f, 1.6f, 1.85f));
        }

        /// <summary>
        /// Paves the square in 2 m kit tiles on the floor's top: uneven brick, with a red-brick road from the square's
        /// centre out through each doorway.
        /// </summary>
        public void Pave(IReadOnlyList<Side> exits)
        {
            for (float x = Tile / 2; x < TownSquare.CellSize; x += Tile)
            {
                for (float z = Tile / 2; z < TownSquare.CellSize; z += Tile)
                {
                    string model = OnRoad(x, z, exits) ? "Floor_RedBrick" : "Floor_UnevenBrick";
                    Decor.Add(new DecorItem("Paving", model, x, TownSquare.FloorTop, z, 0));
                }
            }
        }

        private static bool OnRoad(float x, float z, IReadOnlyList<Side> exits)
        {
            const float Centre = TownSquare.CellSize / 2;
            bool alongX = z > TownRules.DoorwayFrom && z < TownRules.DoorwayTo;
            bool alongZ = x > TownRules.DoorwayFrom && x < TownRules.DoorwayTo;
            return (alongZ && ((exits.Contains(Side.N) && z > Centre - 3) || (exits.Contains(Side.S) && z < Centre + 3)))
                || (alongX && ((exits.Contains(Side.E) && x > Centre - 3) || (exits.Contains(Side.W) && x < Centre + 3)));
        }

        /// <summary>Draws every wall box as 2 m kit-textured wall sections along it.</summary>
        public void DrawWalls(IReadOnlyList<WallSegment> walls)
        {
            foreach (WallSegment wall in walls)
            {
                bool alongX = wall.MaxX - wall.MinX > wall.MaxZ - wall.MinZ;
                float from = alongX ? wall.MinX : wall.MinZ, to = alongX ? wall.MaxX : wall.MaxZ;
                float across = alongX ? (wall.MinZ + wall.MaxZ) / 2 : (wall.MinX + wall.MaxX) / 2;
                for (float at = from + Tile / 2; at < to; at += Tile)
                {
                    Decor.Add(alongX
                        ? new DecorItem("Walls", "avalon_wall_straight_2m", at, 0f, across, 0)
                        : new DecorItem("Walls", "avalon_wall_straight_2m", across, 0f, at, 90));
                }
            }
        }
    }
}
