namespace Avalon.ChunkGen;

/// <summary>
/// Glimmerdell (map 1), the approved layout of 2026-10-01: four 30 m squares, world X/Z 0-60, the arrival point at
/// (15, 15) south-west, the forest portal at (15, 45) north-west, the market south-east and the bank and inn north-east,
/// with the inner walls at X = 30 and Z = 30 opened at 12-18 and 42-48. Buildings are solid facades; porches, stalls and
/// the smithy's lean-to are open-fronted with at least 2.5 m under their roofs. Every solid prop's top is at least
/// 1.05 m up (owner decision 8): the navmesh climbs any step of 1.0 m or less, so a lower bench, crate or floating bar
/// would be walked over. Every coordinate is chunk-local.
/// Edit this file and rerun "dotnet run --project tools/Avalon.ChunkGen -- town"; TownPiecesShould checks the rules and
/// the committed files, TownNpcPlacementShould the NPC spots against it.
/// </summary>
public static class TownPieces
{
    public const float ArrivalX = 15f;
    public const float ArrivalZ = 15f;
    public const float PortalX = 15f;
    public const float PortalZ = 45f;

    private static BoxPiece Box(string building, string part, Material m, float minX, float maxX, float minZ, float maxZ, float y0, float top, bool walkable = false) =>
        new(building, part, m, minX, maxX, minZ, maxZ, y0, top, walkable);

    private static CylinderPiece Cyl(string building, string part, Material m, float x, float z, float r, float y0, float top) =>
        new(building, part, m, x, z, r, y0, top);

    private static RingPiece Ring(string building, string part, Material m, float x, float z, float outer, float inner, float y0, float top) =>
        new(building, part, m, x, z, outer, inner, y0, top);

    private static GablePiece Gable(string building, string part, Material m, float minX, float maxX, float minZ, float maxZ, float eaves, float ridge) =>
        new(building, part, m, minX, maxX, minZ, maxZ, eaves, ridge);

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
        new("town_sw_01", 0, 0, [Side.N, Side.E], [s_wallNL, s_wallNR, s_wallEL, s_wallER, s_wallS, s_wallW], Arrival(), IsEntry: true, HasForwardPortal: false, ["town", "entry"]),
        new("town_se_01", 1, 0, [Side.N, Side.W], [s_wallNL, s_wallNR, s_wallE, s_wallS, s_wallWL, s_wallWR], Market(), IsEntry: false, HasForwardPortal: false, ["town"]),
        new("town_nw_01", 0, 1, [Side.E, Side.S], [s_wallN, s_wallEL, s_wallER, s_wallSL, s_wallSR, s_wallW], Portal(), IsEntry: false, HasForwardPortal: true, ["town"]),
        new("town_ne_01", 1, 1, [Side.S, Side.W], [s_wallN, s_wallE, s_wallSL, s_wallSR, s_wallWL, s_wallWR], BankAndInn(), IsEntry: false, HasForwardPortal: false, ["town"]),
    ];

    /// <summary>South-west: the town hall with its porch, the fountain, benches, lamp posts, the notice board.</summary>
    private static List<TownPiece> Arrival() =>
    [
        // In the square's north-east corner with its porch to the west, facing the camera (it stood on the west side and
        // showed the camera its back). Its body starts at z 19 and its roof at 18.7, north of the walk to the east
        // doorway at z 12-18.
        Box("Town hall", "body", Material.Plaster, 20f, 26.7f, 19f, 27f, 0f, 5.5f),
        Gable("Town hall", "roof", Material.Roof, 19.7f, 27f, 18.7f, 27.3f, 5.5f, 7.6f),
        Box("Town hall", "porch deck", Material.Wood, 18f, 20f, 20.5f, 25.5f, 0f, 0.2f, walkable: true) with { Front = Facing.NegX },
        Box("Town hall", "porch step", Material.Stone, 17.5f, 18f, 20.5f, 25.5f, 0f, 0.1f, walkable: true),
        Cyl("Town hall", "post", Material.Wood, 18.2f, 20.75f, 0.12f, 0f, 3.2f),
        Cyl("Town hall", "post", Material.Wood, 18.2f, 25.25f, 0.12f, 0f, 3.2f),
        Box("Town hall", "porch roof", Material.Roof, 17.6f, 20.1f, 20.2f, 25.8f, 3.2f, 3.5f),
        Ring("Fountain", "basin", Material.Stone, 15f, 7.5f, 2.2f, 1.8f, 0f, 1.1f),   // a 0.4 m rim, 1.1 m up (owner decision 1): the water shows inside it
        Cyl("Fountain", "water", Material.Water, 15f, 7.5f, 1.78f, 0.9f, 1f),         // just inside the rim, its surface 0.1 m below the rim top
        Cyl("Fountain", "column", Material.Stone, 15f, 7.5f, 0.5f, 0f, 2.2f),
        Cyl("Fountain", "upper bowl", Material.Stone, 15f, 7.5f, 0.9f, 1.6f, 1.85f),
        Box("Benches", "bench W", Material.Wood, 10f, 10.5f, 6.6f, 8.4f, 0f, 1.05f),
        Box("Benches", "bench E", Material.Wood, 19.5f, 20f, 6.6f, 8.4f, 0f, 1.05f),
        Box("Benches", "bench S", Material.Wood, 14.1f, 15.9f, 3.4f, 3.9f, 0f, 1.05f),
        Box("Benches", "bench NE", Material.Wood, 21f, 22.8f, 9.5f, 10f, 0f, 1.05f),
        Box("Benches", "bench SE", Material.Wood, 23.1f, 24.9f, 5f, 5.5f, 0f, 1.05f),
        Cyl("Lamp posts", "lamp post 1", Material.Metal, 10.5f, 20.5f, 0.15f, 0f, 3.5f),
        Cyl("Lamp posts", "lamp post 2", Material.Metal, 10.5f, 9.5f, 0.15f, 0f, 3.5f),
        Cyl("Lamp posts", "lamp post 3", Material.Metal, 19.5f, 19.2f, 0.15f, 0f, 3.5f),
        Cyl("Lamp posts", "lamp post 4", Material.Metal, 19.5f, 9.5f, 0.15f, 0f, 3.5f),
        Cyl("Lamp posts", "lamp post 5", Material.Metal, 24.5f, 12.5f, 0.15f, 0f, 3.5f),
        Box("Notice board", "board", Material.Wood, 8.85f, 9.15f, 23f, 25f, 0f, 2.3f),
    ];

    /// <summary>North-west: the gate arch on the path to the portal, the watchtower, the hunter's lodge with its porch, a cart, crates.</summary>
    private static List<TownPiece> Portal() =>
    [
        Box("Gate arch", "pillar W", Material.Stone, 10.5f, 12f, 11.25f, 12.75f, 0f, 4.5f),
        Box("Gate arch", "pillar E", Material.Stone, 18f, 19.5f, 11.25f, 12.75f, 0f, 4.5f),
        Box("Gate arch", "lintel", Material.Stone, 10.5f, 19.5f, 11.25f, 12.75f, 4.5f, 5.5f),
        Box("Watchtower", "body", Material.Stone, 3f, 7f, 22f, 26f, 0f, 9f),
        Gable("Watchtower", "roof", Material.Roof, 2.7f, 7.3f, 21.7f, 26.3f, 9f, 11f),
        Box("Hunter's lodge", "body", Material.Wood, 20.5f, 27.25f, 3f, 11f, 0f, 4f),
        Gable("Hunter's lodge", "roof", Material.Roof, 20.2f, 27.55f, 2.7f, 11.3f, 4f, 6f),
        Box("Hunter's lodge", "porch deck", Material.Wood, 18f, 20.5f, 4.5f, 9.5f, 0f, 0.2f, walkable: true) with { Front = Facing.NegX },
        Box("Hunter's lodge", "porch step", Material.Wood, 17.5f, 18f, 4.5f, 9.5f, 0f, 0.1f, walkable: true),
        Cyl("Hunter's lodge", "post", Material.Wood, 18.25f, 4.75f, 0.12f, 0f, 3.2f),
        Cyl("Hunter's lodge", "post", Material.Wood, 18.25f, 9.25f, 0.12f, 0f, 3.2f),
        Box("Hunter's lodge", "porch roof", Material.Roof, 17.9f, 20.6f, 4.2f, 9.8f, 3.2f, 3.5f),
        Box("Cart", "bed", Material.Wood, 4f, 6.4f, 14f, 17.6f, 0.5f, 1.4f),
        Box("Cart", "wheel W", Material.Wood, 3.8f, 4f, 15.2f, 16.4f, 0f, 1.2f),
        Box("Cart", "wheel E", Material.Wood, 6.4f, 6.6f, 15.2f, 16.4f, 0f, 1.2f),
        Box("Cart", "shaft", Material.Wood, 4.9f, 5.5f, 17.6f, 19.6f, 0f, 1.05f),   // from the floor: a floating bar's top would be a step
        Box("Crates (lodge)", "crate 1", Material.Wood, 7.5f, 8.5f, 15f, 16f, 0f, 1.05f),
        Box("Crates (lodge)", "crate 2", Material.Wood, 8.5f, 9.5f, 15f, 16f, 0f, 1.05f),
        Box("Crates (lodge)", "crate 3 (stacked)", Material.Wood, 8f, 9f, 15f, 16f, 1.05f, 2.05f),
        Cyl("Lamp posts", "lamp post 1", Material.Metal, 9f, 11f, 0.15f, 0f, 3.5f),
        Cyl("Lamp posts", "lamp post 2", Material.Metal, 21f, 12.5f, 0.15f, 0f, 3.5f),
        Cyl("Lamp posts", "lamp post 3", Material.Metal, 11f, 18f, 0.15f, 0f, 3.5f),
        Cyl("Lamp posts", "lamp post 4", Material.Metal, 19f, 18f, 0.15f, 0f, 3.5f),
        Box("Benches", "bench lodge", Material.Wood, 22f, 23.8f, 12.5f, 13f, 0f, 1.05f),
    ];

    /// <summary>South-east: the smithy with its lean-to, the armourer's stall, the general-goods stall, crates and barrels.</summary>
    private static List<TownPiece> Market() =>
    [
        // On the market's north side with its lean-to to the south, facing the camera (it stood on the south side and
        // showed the camera its back); its north wall stops at 26.7, clear of the north doorway lane.
        Box("Smithy", "body", Material.Stone, 6f, 14f, 21.5f, 26.7f, 0f, 4f),
        Gable("Smithy", "roof", Material.Roof, 5.7f, 14.3f, 21.2f, 27f, 4f, 5.8f),
        Cyl("Smithy", "chimney", Material.Stone, 12.5f, 26f, 0.5f, 0f, 7f),
        Cyl("Smithy", "post", Material.Wood, 7.2f, 19.2f, 0.12f, 0f, 3f),
        Cyl("Smithy", "post", Material.Wood, 10.8f, 19.2f, 0.12f, 0f, 3f),
        Box("Smithy", "lean-to roof", Material.Wood, 6.9f, 11.1f, 18.9f, 21.5f, 3f, 3.25f) with { Front = Facing.NegZ },
        Box("Smithy", "anvil", Material.Metal, 7.2f, 8f, 20.2f, 20.6f, 0f, 1.05f),
        Box("Smithy", "quench trough", Material.Wood, 12.8f, 14.3f, 20.8f, 21.4f, 0f, 1.05f),
        Box("Smithy", "wood pile", Material.Wood, 3.5f, 5.5f, 26f, 27f, 0f, 1.05f),
        Cyl("Barrels (smithy)", "barrel 1", Material.Wood, 4.3f, 24f, 0.4f, 0f, 1.05f),   // west of it: the walk to the north doorway stays open
        Cyl("Barrels (smithy)", "barrel 2", Material.Wood, 4.3f, 23.1f, 0.4f, 0f, 1.05f),
        Box("Armourer's stall", "counter", Material.Wood, 25f, 25.4f, 13.5f, 16.5f, 0f, 1.05f) with { Front = Facing.NegX },
        Cyl("Armourer's stall", "post", Material.Wood, 25.2f, 13.6f, 0.1f, 0f, 2.9f),
        Cyl("Armourer's stall", "post", Material.Wood, 25.2f, 16.4f, 0.1f, 0f, 2.9f),
        Cyl("Armourer's stall", "post", Material.Wood, 27.3f, 13.6f, 0.1f, 0f, 2.9f),
        Cyl("Armourer's stall", "post", Material.Wood, 27.3f, 16.4f, 0.1f, 0f, 2.9f),
        Box("Armourer's stall", "roof", Material.Cloth, 24.6f, 27.6f, 13.1f, 16.9f, 2.9f, 3.15f),
        Box("Crates (armourer)", "crate 1", Material.Wood, 25f, 26f, 19.5f, 20.5f, 0f, 1.05f),
        Box("Crates (armourer)", "crate 2", Material.Wood, 26f, 27f, 19.5f, 20.5f, 0f, 1.05f),
        Box("General-goods stall", "counter", Material.Wood, 20f, 23f, 24f, 24.4f, 0f, 1.05f) with { Front = Facing.NegZ },
        Cyl("General-goods stall", "post", Material.Wood, 20.1f, 24.2f, 0.1f, 0f, 2.9f),
        Cyl("General-goods stall", "post", Material.Wood, 22.9f, 24.2f, 0.1f, 0f, 2.9f),
        Cyl("General-goods stall", "post", Material.Wood, 20.1f, 26.4f, 0.1f, 0f, 2.9f),
        Cyl("General-goods stall", "post", Material.Wood, 22.9f, 26.4f, 0.1f, 0f, 2.9f),
        Box("General-goods stall", "awning", Material.Cloth2, 19.4f, 23.6f, 23.2f, 27f, 2.9f, 3.2f),   // owner decision 2
        Box("Crates (general goods)", "crate 1", Material.Wood, 24.5f, 25.5f, 25f, 26f, 0f, 1.05f),
        Box("Crates (general goods)", "crate 2", Material.Wood, 25.5f, 26.5f, 25f, 26f, 0f, 1.05f),
        Cyl("Barrels (general goods)", "barrel", Material.Wood, 24.2f, 23.4f, 0.4f, 0f, 1.05f),
        Box("Benches", "bench market", Material.Wood, 4.1f, 5.9f, 15.75f, 16.25f, 0f, 1.05f),
        Cyl("Lamp posts", "lamp post 1", Material.Metal, 5.5f, 18.5f, 0.15f, 0f, 3.5f),
        Cyl("Lamp posts", "lamp post 2", Material.Metal, 22f, 20f, 0.15f, 0f, 3.5f),
    ];

    /// <summary>North-east: the bank with its steps, the inn with its porch, two houses, the well, crates and barrels.</summary>
    private static List<TownPiece> BankAndInn() =>
    [
        // Turned in place so its steps are on the south, facing the camera (they were on the east, facing away).
        Box("Bank", "body", Material.Stone, 2.8f, 10.8f, 20.5f, 26.5f, 0f, 5f),
        Gable("Bank", "roof", Material.Roof, 2.5f, 11.1f, 20.2f, 26.8f, 5f, 6.5f),
        Box("Bank", "lower step", Material.Stone, 4.8f, 8.8f, 19f, 20.5f, 0f, 0.15f, walkable: true) with { Front = Facing.NegZ },
        Box("Bank", "upper step", Material.Stone, 4.8f, 8.8f, 19.75f, 20.5f, 0f, 0.3f, walkable: true),
        Box("Bank", "pilaster W", Material.Stone, 4.1f, 4.7f, 19.9f, 20.5f, 0f, 5f),
        Box("Bank", "pilaster E", Material.Stone, 8.9f, 9.5f, 19.9f, 20.5f, 0f, 5f),
        Box("Inn", "body", Material.Plaster, 14f, 26f, 21f, 27.25f, 0f, 7f),
        Gable("Inn", "roof", Material.Roof, 13.7f, 26.3f, 20.7f, 27.55f, 7f, 9.5f),
        Cyl("Inn", "chimney", Material.Stone, 24f, 24f, 0.45f, 0f, 10.5f),
        Box("Inn", "porch deck", Material.Wood, 16f, 24f, 18.5f, 21f, 0f, 0.2f, walkable: true) with { Front = Facing.NegZ },
        Box("Inn", "porch step", Material.Wood, 16f, 24f, 18f, 18.5f, 0f, 0.1f, walkable: true),
        Cyl("Inn", "post", Material.Wood, 16.3f, 18.8f, 0.12f, 0f, 3.2f),
        Cyl("Inn", "post", Material.Wood, 18.8f, 18.8f, 0.12f, 0f, 3.2f),
        Cyl("Inn", "post", Material.Wood, 21.2f, 18.8f, 0.12f, 0f, 3.2f),
        Cyl("Inn", "post", Material.Wood, 23.7f, 18.8f, 0.12f, 0f, 3.2f),
        Box("Inn", "porch roof", Material.Roof, 15.8f, 24.2f, 18.2f, 21.1f, 3.2f, 3.5f),
        Box("Benches", "bench inn", Material.Wood, 25.3f, 27.1f, 19.2f, 19.7f, 0f, 1.05f),
        Cyl("Barrel (inn)", "barrel", Material.Wood, 27f, 21.5f, 0.4f, 0f, 1.05f),
        Box("House A", "body", Material.Plaster, 3.5f, 9.5f, 3f, 9f, 0f, 3.2f),
        Gable("House A", "roof", Material.Roof, 3.2f, 9.8f, 2.7f, 9.3f, 3.2f, 5f),
        Cyl("House A", "chimney", Material.Stone, 8.3f, 4.5f, 0.35f, 0f, 5.6f),
        Box("House B", "body", Material.Plaster, 21f, 27f, 3f, 9f, 0f, 3.2f),
        Gable("House B", "roof", Material.Roof, 20.7f, 27.3f, 2.7f, 9.3f, 3.2f, 5f),
        Cyl("House B", "chimney", Material.Stone, 22.2f, 4.5f, 0.35f, 0f, 5.6f),
        Box("Crates (house A)", "crate 1", Material.Wood, 4f, 5f, 10f, 11f, 0f, 1.05f),
        Box("Crates (house A)", "crate 2", Material.Wood, 5f, 6f, 10f, 11f, 0f, 1.05f),
        Cyl("Barrels (house B)", "barrel 1", Material.Wood, 20f, 6.5f, 0.4f, 0f, 1.05f),
        Cyl("Barrels (house B)", "barrel 2", Material.Wood, 20f, 7.4f, 0.4f, 0f, 1.05f),
        Cyl("Well", "ring", Material.Stone, 15f, 14f, 1f, 0f, 1.05f),
        Cyl("Well", "post", Material.Wood, 15f, 12.75f, 0.1f, 0f, 2.5f),
        Cyl("Well", "post", Material.Wood, 15f, 15.25f, 0.1f, 0f, 2.5f),
        Box("Well", "roof", Material.Roof, 14.2f, 15.8f, 12.4f, 15.6f, 2.5f, 2.8f),
        Box("Benches", "bench well", Material.Wood, 17.75f, 18.25f, 13.1f, 14.9f, 0f, 1.05f),
        Cyl("Lamp posts", "lamp post 1", Material.Metal, 11f, 17f, 0.15f, 0f, 3.5f),
        Cyl("Lamp posts", "lamp post 2", Material.Metal, 19.5f, 10.5f, 0.15f, 0f, 3.5f),
    ];
}
