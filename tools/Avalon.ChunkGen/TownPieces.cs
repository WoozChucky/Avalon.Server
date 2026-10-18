namespace Avalon.ChunkGen;

/// <summary>
/// Glimmerdell (map 1) built from the medieval village kit (design of 2026-10-17): four 30 m squares, world X/Z 0-60,
/// the arrival point at (15, 15) south-west, the forest portal at (15, 45) north-west, the market south-east and the
/// bank and inn north-east, with the inner walls at X = 30 and Z = 30 opened at 12-18 and 42-48. Each building is a
/// recipe (Recipes/*.json) placed with <see cref="TownBuilding.Place" />, its front on the -X or -Z side the camera sees;
/// each prop is a kit or avalon_* model with a collision box around it, or, where parts of it stand overhead (the well,
/// the lamp posts, the stalls), with shapes of its real parts; wooden fences close the yards of the two houses and the
/// smithy's wood pile and an iron railing stands beside the bank, each 2 m section with its own box; stone curbs, drawn
/// only, line the roads where they run past open ground; the walls and the paving are decor over today's wall and floor
/// boxes. Every solid's top is at least 1.05 m up (owner decision 8): the navmesh climbs any step of 1.0 m or less.
/// Every coordinate is chunk-local.
/// Edit this file and rerun "dotnet run --project tools/Avalon.ChunkGen -- town"; TownPiecesShould checks the rules and
/// the committed files, TownNpcPlacementShould the NPC spots against it.
/// </summary>
public static class TownPieces
{
    public const float ArrivalX = 15f;
    public const float ArrivalZ = 15f;
    public const float PortalX = 15f;
    // In the gate: the portal square's north wall is world Z 60, and the arch stands just inside it, its middle here.
    public const float PortalZ = 58.75f;

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
        Square("town_nw_01", 0, 1, [Side.E, Side.S], [s_wallNL, s_wallNR, s_wallEL, s_wallER, s_wallSL, s_wallSR, s_wallW], Portal, isEntry: false, hasForwardPortal: true, ["town"], gate: Side.N),
        Square("town_ne_01", 1, 1, [Side.S, Side.W], [s_wallN, s_wallE, s_wallSL, s_wallSR, s_wallWL, s_wallWR], BankAndInn, isEntry: false, hasForwardPortal: false, ["town"]),
    ];

    private static TownSquare Square(
        string name, int gridX, int gridZ, IReadOnlyList<Side> exits, IReadOnlyList<WallSegment> walls, Action<SquareBuilder> build,
        bool isEntry, bool hasForwardPortal, IReadOnlyList<string> tags, Side? gate = null)
    {
        var square = new SquareBuilder();
        build(square);
        IReadOnlyList<Side> roads = gate is { } g ? [.. exits, g] : exits;
        square.Pave(roads);
        square.DrawWalls(walls);
        square.Curbs(roads, walls);
        return new TownSquare(name, gridX, gridZ, exits, walls, square.Pieces, isEntry, hasForwardPortal, tags)
        {
            Gate = gate,
            Decor = square.Decor,
            WallsAsDecor = true,
            Paved = true,
        };
    }

    /// <summary>South-west: the town hall, the fountain with its benches, torches and a brazier, the notice board, a house.</summary>
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
        // Torches at the roads' edges, off the red brick: the north road's west side and the east road's south side.
        s.Torch("torch 1", 10.5f, 20.5f);
        s.Torch("torch 2", 10.5f, 10f);
        s.Torch("torch 3", 20.5f, 11f);
        s.Torch("torch 4", 25.5f, 11f);
        s.Brazier("brazier", 6f, 5f);
        s.Prop("avalon_notice_board", "Notice board", "board", 9f, 24f, 90);
        // A house in the south-east corner, south of the east doorway lane: south-west of every NPC is kept low, so
        // the camera sees each one over it.
        s.Building("house_a", "House A", 23.5f, 7.1f, 0);
        // Its yard against its west wall, south of the east bench and east of the fountain.
        s.WoodenFence("Fence (house A)", "west", 18.2f, 2.4f, alongZ: true, sections: 2);
        s.WoodenFence("Fence (house A)", "south", 18.2f, 2.4f, alongZ: false, sections: 1);
        s.WoodenFence("Fence (house A)", "north", 18.2f, 6.4f, alongZ: false, sections: 1);
    }

    /// <summary>North-west: the gate on the road to the portal, the watchtower, the hunter's lodge, a cart, crates, fires.</summary>
    private static void Portal(SquareBuilder s)
    {
        // The way out of town: the road runs north to the gate in the north wall (its lintel 3.12 m up over X 12-18),
        // the arch just inside the opening with its north face on the wall line, and the portal in it.
        s.Building("gate_arch", TownRules.GateArch, 15f, 28.765f, 0);
        s.Building("watchtower", "Watchtower", 6f, 24f, 0);
        // Facing west, its awning over the door; south of the east doorway lane and north of the south one.
        s.Building("hunters_lodge", "Hunter's lodge", 22.7f, 7.2f, 0);
        s.Prop("Prop_Wagon", "Cart", "wagon", 5f, 17f, 0);
        s.Prop("Prop_Crate", "Crates (lodge)", "crate 1", 8.5f, 15.5f, 0);
        s.Prop("Prop_Crate", "Crates (lodge)", "crate 2", 9.7f, 15.5f, 90);
        s.Torch("torch 1", 11f, 18f);
        s.Torch("torch 2", 19f, 18.6f);
        s.Torch("torch 3", 11f, 5f);
        s.Brazier("brazier", 7f, 9f);
        // South of the east road, before the lodge.
        s.Prop("avalon_bench", "Benches", "bench lodge", 23.5f, 10.4f, 0);
    }

    /// <summary>
    /// South-east: the smithy with its lean-to, its forge tools and its fenced wood pile, the armourer's stall, the
    /// general-goods stall.
    /// </summary>
    /// <remarks>
    /// The walking benchmarks (tests/Avalon.World.Testing, TownNavmesh.LoopCentre) walk an 8 m loop about world (45, 10),
    /// this square's (15, 10): nothing solid stands 7-9 m from it, a metre clear either side of the walkers.
    /// </remarks>
    private static void Market(SquareBuilder s)
    {
        // In the square's north-east corner, its roof 2 m from the east wall, its lean-to to the south (the camera's
        // side) over the anvil, its tools at its sides; its wood pile fenced between its west wall and the road.
        s.Building("smithy", "Smithy", 23.6f, 22.8f, 0);
        s.Prop("avalon_anvil", "Smithy", "anvil", 25.3f, 17.8f, 0);
        s.Prop("avalon_quench_trough", "Smithy", "quench trough", 20.5f, 20f, 0);
        s.Prop("avalon_wood_pile", "Smithy", "wood pile", 19.3f, 24.5f, 0);
        s.WoodenFence("Fence (smithy)", "west", 18.2f, 23.47f, alongZ: true, sections: 1);
        s.WoodenFence("Fence (smithy)", "south", 18.25f, 23.47f, alongZ: false, sections: 1);
        s.WoodenFence("Fence (smithy)", "north", 18.25f, 25.53f, alongZ: false, sections: 1);
        // East of the north road, against the wood pile's fence.
        s.Prop("avalon_barrel", "Barrels (smithy)", "barrel 1", 19f, 21.2f, 0);
        s.Prop("avalon_barrel", "Barrels (smithy)", "barrel 2", 19f, 22.2f, 0);
        // Counters to the west and to the south, each merchant in front of it on the camera's side.
        s.Stall("avalon_stall_armourer", "Armourer's stall", 25.5f, 15f, 270, Facing.NegX);
        s.Prop("Prop_Crate", "Crates (armourer)", "crate 1", 26f, 5f, 0);
        s.Prop("Prop_Crate", "Crates (armourer)", "crate 2", 26f, 3.8f, 90);
        s.Stall("avalon_stall_goods", "General-goods stall", 7f, 24f, 180, Facing.NegZ);
        s.Prop("Prop_Crate", "Crates (general goods)", "crate 1", 10.5f, 25f, 0);
        s.Prop("avalon_barrel", "Barrels (general goods)", "barrel", 10.5f, 23.3f, 0);
        s.Prop("avalon_bench", "Benches", "bench market", 5f, 10f, 90);
        s.Torch("torch 1", 4.5f, 19.5f);
        s.Torch("torch 2", 11.5f, 20.5f);
        // By the forge, more than 9 m from the walkers' loop.
        s.Brazier("brazier", 24f, 19.5f);
    }

    /// <summary>North-east: the bank with its railing, the inn, a house with its yard, the well in the open south-west.</summary>
    private static void BankAndInn(SquareBuilder s)
    {
        s.Building("bank", "Bank", 8f, 21.5f, 0);
        // An iron railing off the bank's east wall, north of the road, closing a strip beside it.
        s.MetalFence("Railing (bank)", "south", 12.25f, 19f, alongZ: false, sections: 1);
        s.MetalFence("Railing (bank)", "east", 14.25f, 19f, alongZ: true, sections: 4);
        s.Building("inn", "Inn", 21.5f, 20.5f, 0);
        s.Building("house_b", "House B", 24.5f, 6.5f, 0);
        // Its yard against its west wall, the crate and barrels inside it, east of the road.
        s.WoodenFence("Fence (house B)", "west", 18.2f, 4.2f, alongZ: true, sections: 2);
        s.WoodenFence("Fence (house B)", "south", 18.2f, 4.2f, alongZ: false, sections: 2);
        s.WoodenFence("Fence (house B)", "north", 18.2f, 8.3f, alongZ: false, sections: 2);
        // In the open ground south of the west road and west of the south road, clear of both.
        s.Well(6.5f, 6.5f);
        s.Prop("avalon_bench", "Benches", "bench well", 9.6f, 6.5f, 270);
        s.Brazier("brazier", 6.5f, 10.6f);
        s.Prop("Prop_Crate", "Crates (house B)", "crate 1", 19.5f, 7.2f, 0);
        s.Prop("avalon_barrel", "Barrels (house B)", "barrel 1", 19.5f, 5f, 0);
        s.Prop("avalon_barrel", "Barrels (house B)", "barrel 2", 19.5f, 6f, 0);
        // The corner where the two roads meet, off both.
        s.Torch("torch 1", 11.2f, 11.2f);
        s.Torch("torch 2", 18.5f, 3.5f);
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
            Pieces.AddRange(proxies.Select(p => p with { PartOfBuilding = true }));
            Decor.AddRange(decor);
        }

        /// <summary>A model with one collision box around its turned bounds, standing at least <see cref="MinSolidTop" />.</summary>
        public void Prop(string model, string building, string part, float x, float z, int yaw)
        {
            var item = new DecorItem(building, model, x, 0f, z, yaw);
            (float minX, float maxX, _, float maxY, float minZ, float maxZ) = DecorRules.WorldBounds(item);
            Decor.Add(item);
            Pieces.Add(new BoxPiece(building, part, Material.Collision, minX, maxX, minZ, maxZ, 0f, Math.Max(maxY, MinSolidTop)));
        }

        /// <summary>
        /// A lamp post (avalon_lamp_post, unturned, its lantern arm toward +Z): the base, raised to
        /// <see cref="MinSolidTop" />; the post to its finial, thinner than a navmesh voxel but rasterised all the same;
        /// and the arm with its lantern as one slab at their height, 2.75-3.16 m, overhead.
        /// </summary>
        public void LampPost(string part, float x, float z)
        {
            const string LampPosts = "Lamp posts";
            Decor.Add(new DecorItem(LampPosts, "avalon_lamp_post", x, 0f, z, 0));
            Pieces.Add(new CylinderPiece(LampPosts, $"{part} base", Material.Collision, x, z, 0.22f, 0f, MinSolidTop));
            Pieces.Add(new CylinderPiece(LampPosts, $"{part} post", Material.Collision, x, z, 0.07f, 0f, 3.5f));
            Pieces.Add(new BoxPiece(LampPosts, $"{part} lantern", Material.Collision, x - 0.15f, x + 0.15f, z, z + 0.6f, 2.75f, 3.16f));
        }

        /// <summary>
        /// A standing torch (avalon_torch): its stone foot raised to <see cref="MinSolidTop" />, and the pole to the top of
        /// its fire cup (2.2 m; the cup has no piece of its own, which would be an overhead with no headroom under it).
        /// The client burns a flame at the cup's light.
        /// </summary>
        public void Torch(string part, float x, float z)
        {
            const string Torches = "Torches";
            Decor.Add(new DecorItem(Torches, "avalon_torch", x, 0f, z, 0));
            Pieces.Add(new CylinderPiece(Torches, $"{part} foot", Material.Collision, x, z, 0.18f, 0f, MinSolidTop));
            Pieces.Add(new CylinderPiece(Torches, $"{part} pole", Material.Collision, x, z, 0.06f, 0f, 2.2f));
        }

        /// <summary>
        /// A brazier (avalon_brazier): the iron bowl on its legs as one cylinder, raised to <see cref="MinSolidTop" />.
        /// The client burns a flame at its light.
        /// </summary>
        public void Brazier(string part, float x, float z)
        {
            const string Braziers = "Braziers";
            Decor.Add(new DecorItem(Braziers, "avalon_brazier", x, 0f, z, 0));
            Pieces.Add(new CylinderPiece(Braziers, $"{part} bowl", Material.Collision, x, z, 0.4f, 0f, MinSolidTop));
        }

        /// <summary>
        /// The well (avalon_well, unturned): its rim, 1.05 m up; the two posts north and south of it; and its roof as a
        /// slab at 2.5-2.8 m, the art's 1.6 m wide roof widened to 1.9 m so the rim's corners lie within the tolerance of
        /// it (DecorRules), overhead where nobody stands.
        /// </summary>
        public void Well(float x, float z)
        {
            Decor.Add(new DecorItem("Well", "avalon_well", x, 0f, z, 0));
            Pieces.Add(new CylinderPiece("Well", "rim", Material.Collision, x, z, 1.05f, 0f, MinSolidTop));
            Pieces.Add(new CylinderPiece("Well", "post N", Material.Collision, x, z + 1.15f, 0.1f, 0f, 2.5f));
            Pieces.Add(new CylinderPiece("Well", "post S", Material.Collision, x, z - 1.15f, 0.1f, 0f, 2.5f));
            Pieces.Add(new BoxPiece("Well", "roof", Material.Collision, x - 0.95f, x + 0.95f, z - 1.6f, z + 1.6f, 2.5f, 2.8f));
        }

        /// <summary>
        /// A market stall (avalon_stall_*), turned by <paramref name="yaw" />: its counter (the stall's front, toward +Z
        /// unturned) 1.1 m up, its four posts, and its cloth roof as a slab at 2.9-3.4 m, so the booth behind the counter
        /// stays open under it.
        /// </summary>
        public void Stall(string model, string building, float x, float z, int yaw, Facing front)
        {
            Decor.Add(new DecorItem(building, model, x, 0f, z, yaw));
            Pieces.Add(Turned(building, "counter", x, z, yaw, -1.55f, 1.55f, 1.45f, 1.95f, 0f, 1.1f) with { Front = front });
            foreach ((float px, float pz) in new[] { (-1.4f, -1.3f), (1.4f, -1.3f), (-1.4f, 1.8f), (1.4f, 1.8f) })
            {
                (float wx, _, float wz, _) = TownBuilding.Footprint(px, px, pz, pz, x, z, yaw / 90);
                Pieces.Add(new CylinderPiece(building, "post", Material.Collision, wx, wz, 0.08f, 0f, 2.9f));
            }
            Pieces.Add(Turned(building, "roof", x, z, yaw, -1.5f, 1.5f, -1.9f, 1.9f, 2.9f, 3.4f));
        }

        /// <summary>A box given in the model's own space, turned by its yaw and moved to (x, z) as its decor is.</summary>
        private static BoxPiece Turned(
            string building, string part, float x, float z, int yaw, float minX, float maxX, float minZ, float maxZ, float y0, float top)
        {
            (float wMinX, float wMaxX, float wMinZ, float wMaxZ) = TownBuilding.Footprint(minX, maxX, minZ, maxZ, x, z, yaw / 90);
            return new BoxPiece(building, part, Material.Collision, wMinX, wMaxX, wMinZ, wMaxZ, y0, top);
        }

        /// <summary>
        /// A run of wooden fence sections, 2 m apart, from (<paramref name="x" />, <paramref name="z" />) east along X or
        /// north along Z: a single section first, then extensions. Each is a <see cref="Prop" />: the fence stands
        /// 0.81 m, its box <see cref="MinSolidTop" />.
        /// </summary>
        public void WoodenFence(string building, string side, float x, float z, bool alongZ, int sections) =>
            FenceRun(building, side, "Prop_WoodenFence_Single", ["Prop_WoodenFence_Extension1", "Prop_WoodenFence_Extension2"], x, z, alongZ, sections);

        /// <summary>A run of iron railing sections, laid as <see cref="WoodenFence" /> lays wooden ones, plain and ornamented in turn.</summary>
        public void MetalFence(string building, string side, float x, float z, bool alongZ, int sections) =>
            FenceRun(building, side, "Prop_MetalFence_Simple", ["Prop_MetalFence_Ornament", "Prop_MetalFence_Simple"], x, z, alongZ, sections);

        private void FenceRun(string building, string side, string first, string[] then, float x, float z, bool alongZ, int sections)
        {
            for (int i = 0; i < sections; i++)
            {
                // The first model starts the run, the others take turns after it.
                string model = i == 0 ? first : then[(i - 1) % then.Length];
                float at = Tile / 2 + Tile * i;
                // A section runs along its own X: a quarter turn lays it along Z.
                if (alongZ) Prop(model, building, $"{side} {i + 1}", x, z + at, 90);
                else Prop(model, building, $"{side} {i + 1}", x + at, z, 0);
            }
        }

        /// <summary>
        /// Stone curbs along the edges of the square's red-brick roads: one curb section on the open side of each 2 m
        /// tile's edge (unturned, a curb's 0.7 m depth lies on the +Z side of its line), and a corner piece where the
        /// road turns outward. A section that would meet a wall, a solid piece at body height or a curb already laid is
        /// left out, so the curbs line the roads only where they run past open ground. Dressing: drawn, not baked, a
        /// 0.134 m riser a player walks over.
        /// </summary>
        public void Curbs(IReadOnlyList<Side> exits, IReadOnlyList<WallSegment> walls)
        {
            const string CurbsBuilding = "Curbs";
            int n = (int)(TownSquare.CellSize / Tile);
            bool Road(int i, int j) => TownSquare.OnRoad(Tile * i + Tile / 2, Tile * j + Tile / 2, exits);
            bool Open(int i, int j) => i >= 0 && j >= 0 && i < n && j < n && !Road(i, j);

            List<(float MinX, float MaxX, float MinZ, float MaxZ)> laid = [];
            void Lay(string model, float x, float z, int yaw)
            {
                var item = new DecorItem(CurbsBuilding, model, x, TownSquare.FloorTop, z, yaw);
                (float minX, float maxX, _, _, float minZ, float maxZ) = DecorRules.WorldBounds(item);
                bool Meets(float aMinX, float aMaxX, float aMinZ, float aMaxZ) => minX < aMaxX && maxX > aMinX && minZ < aMaxZ && maxZ > aMinZ;
                const float G = DecorRules.Tolerance;
                if (walls.Any(w => Meets(w.MinX, w.MaxX, w.MinZ, w.MaxZ))
                    || Pieces.Any(p => p.Y0 < DecorRules.BodyHeight && Meets(p.Bounds.MinX - G, p.Bounds.MaxX + G, p.Bounds.MinZ - G, p.Bounds.MaxZ + G))
                    || laid.Any(l => Meets(l.MinX, l.MaxX, l.MinZ, l.MaxZ)))
                {
                    return;
                }

                laid.Add((minX, maxX, minZ, maxZ));
                Decor.Add(item);
            }

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    if (!Road(i, j)) continue;
                    float x0 = Tile * i, z0 = Tile * j;
                    string straight = (i + j) % 2 == 0 ? "Prop_ExteriorBorder_Straight1" : "Prop_ExteriorBorder_Straight2";
                    // A quarter turn takes a curb's +Z side to +X, a half turn to -Z, three quarters to -X.
                    if (Open(i, j - 1)) Lay(straight, x0 + Tile / 2, z0, 180);
                    if (Open(i, j + 1)) Lay(straight, x0 + Tile / 2, z0 + Tile, 0);
                    if (Open(i - 1, j)) Lay(straight, x0, z0 + Tile / 2, 270);
                    if (Open(i + 1, j)) Lay(straight, x0 + Tile, z0 + Tile / 2, 90);
                    // The corner piece fills the square two curbs leave open round an outward corner: unturned it lies
                    // north-west of its point, then north-east, south-east and south-west per quarter turn.
                    if (Open(i - 1, j) && Open(i, j - 1) && Open(i - 1, j - 1)) Lay("Prop_ExteriorBorder_Corner", x0, z0, 270);
                    if (Open(i - 1, j) && Open(i, j + 1) && Open(i - 1, j + 1)) Lay("Prop_ExteriorBorder_Corner", x0, z0 + Tile, 0);
                    if (Open(i + 1, j) && Open(i, j + 1) && Open(i + 1, j + 1)) Lay("Prop_ExteriorBorder_Corner", x0 + Tile, z0 + Tile, 90);
                    if (Open(i + 1, j) && Open(i, j - 1) && Open(i + 1, j - 1)) Lay("Prop_ExteriorBorder_Corner", x0 + Tile, z0, 180);
                }
            }
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
                    string model = TownSquare.OnRoad(x, z, exits) ? "Floor_RedBrick" : "Floor_UnevenBrick";
                    Decor.Add(new DecorItem(DecorRules.Paving, model, x, TownSquare.FloorTop, z, 0));
                }
            }
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
