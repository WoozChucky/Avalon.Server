namespace Avalon.ChunkGen;

/// <summary>
/// Glimmerdell (map 1), the approved layout of 2026-10-01: four 30 m squares, world X/Z 0-60, the arrival point at
/// (15, 15) south-west, the forest portal at (15, 45) north-west, the market south-east and the bank and inn north-east,
/// with the inner walls at X = 30 and Z = 30 opened at 12-18 and 42-48. Buildings are solid facades; porches, stalls and
/// the smithy's lean-to are open-fronted with at least 2.5 m under their roofs. Every solid prop is at least 1.05 m
/// tall (owner decision 8): the navmesh climbs any step of 1.0 m or less, so a lower bench or crate would be walked
/// over. Every coordinate is chunk-local.
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

    private static GablePiece Gable(string building, string part, Material m, float minX, float maxX, float minZ, float maxZ, float eaves, float ridge) =>
        new(building, part, m, minX, maxX, minZ, maxZ, eaves, ridge);

    private static WallSegment Wall(string name, float minX, float maxX, float minZ, float maxZ) => new(name, minX, maxX, minZ, maxZ);

    // Today's wall boxes (0.5 m thick, centred on the cell edge; the inner walls open at 12-18).
    private static readonly WallSegment WallN = Wall("Wall_N", 0, 30, 29.75f, 30.25f);
    private static readonly WallSegment WallNL = Wall("Wall_N_L", 0, 12, 29.75f, 30.25f);
    private static readonly WallSegment WallNR = Wall("Wall_N_R", 18, 30, 29.75f, 30.25f);
    private static readonly WallSegment WallS = Wall("Wall_S", 0, 30, -0.25f, 0.25f);
    private static readonly WallSegment WallSL = Wall("Wall_S_L", 0, 12, -0.25f, 0.25f);
    private static readonly WallSegment WallSR = Wall("Wall_S_R", 18, 30, -0.25f, 0.25f);
    private static readonly WallSegment WallE = Wall("Wall_E", 29.75f, 30.25f, 0, 30);
    private static readonly WallSegment WallEL = Wall("Wall_E_L", 29.75f, 30.25f, 0, 12);
    private static readonly WallSegment WallER = Wall("Wall_E_R", 29.75f, 30.25f, 18, 30);
    private static readonly WallSegment WallW = Wall("Wall_W", -0.25f, 0.25f, 0, 30);
    private static readonly WallSegment WallWL = Wall("Wall_W_L", -0.25f, 0.25f, 0, 12);
    private static readonly WallSegment WallWR = Wall("Wall_W_R", -0.25f, 0.25f, 18, 30);

    public static IReadOnlyList<TownSquare> Squares() =>
    [
        new("town_sw_01", 0, 0, [Side.N, Side.E], [WallNL, WallNR, WallEL, WallER, WallS, WallW], Arrival(), IsEntry: true, HasForwardPortal: false, ["town", "entry"]),
        new("town_se_01", 1, 0, [Side.N, Side.W], [WallNL, WallNR, WallE, WallS, WallWL, WallWR], Market(), IsEntry: false, HasForwardPortal: false, ["town"]),
        new("town_nw_01", 0, 1, [Side.E, Side.S], [WallN, WallEL, WallER, WallSL, WallSR, WallW], Portal(), IsEntry: false, HasForwardPortal: true, ["town"]),
        new("town_ne_01", 1, 1, [Side.S, Side.W], [WallN, WallE, WallSL, WallSR, WallWL, WallWR], BankAndInn(), IsEntry: false, HasForwardPortal: false, ["town"]),
    ];

    /// <summary>South-west: the town hall with its porch, the fountain, benches, lamp posts, the notice board.</summary>
    private static List<TownPiece> Arrival() =>
    [
        Box("Town hall", "body", Material.Plaster, 2.8f, 10f, 8f, 22f, 0f, 5.5f),
        Gable("Town hall", "roof", Material.Roof, 2.5f, 10.3f, 7.7f, 22.3f, 5.5f, 7.6f),
        Box("Town hall", "porch deck", Material.Wood, 10f, 12f, 11.5f, 18.5f, 0f, 0.2f, walkable: true),
        Box("Town hall", "porch step", Material.Stone, 12f, 12.5f, 11.5f, 18.5f, 0f, 0.1f, walkable: true),
        Cyl("Town hall", "post", Material.Wood, 11.8f, 11.75f, 0.12f, 0f, 3.2f),
        Cyl("Town hall", "post", Material.Wood, 11.8f, 18.25f, 0.12f, 0f, 3.2f),
        Box("Town hall", "porch roof", Material.Roof, 9.9f, 12.4f, 11.2f, 18.8f, 3.2f, 3.5f),
        Cyl("Fountain", "basin", Material.Stone, 15f, 7.5f, 2.2f, 0f, 1.1f),      // owner decision 1: 1.1 m, above the navmesh step
        Cyl("Fountain", "water", Material.Water, 15f, 7.5f, 2f, 0.9f, 1f),
        Cyl("Fountain", "column", Material.Stone, 15f, 7.5f, 0.5f, 0f, 2.2f),
        Cyl("Fountain", "upper bowl", Material.Stone, 15f, 7.5f, 0.9f, 1.6f, 1.85f),
        Box("Benches", "bench W", Material.Wood, 10f, 10.5f, 6.6f, 8.4f, 0f, 1.05f),
        Box("Benches", "bench E", Material.Wood, 19.5f, 20f, 6.6f, 8.4f, 0f, 1.05f),
        Box("Benches", "bench S", Material.Wood, 14.1f, 15.9f, 3.4f, 3.9f, 0f, 1.05f),
        Box("Benches", "bench NE", Material.Wood, 23.1f, 24.9f, 24.5f, 25f, 0f, 1.05f),
        Box("Benches", "bench SE", Material.Wood, 23.1f, 24.9f, 5f, 5.5f, 0f, 1.05f),
        Cyl("Lamp posts", "lamp post 1", Material.Metal, 10.5f, 20.5f, 0.15f, 0f, 3.5f),
        Cyl("Lamp posts", "lamp post 2", Material.Metal, 10.5f, 9.5f, 0.15f, 0f, 3.5f),
        Cyl("Lamp posts", "lamp post 3", Material.Metal, 19.5f, 20.5f, 0.15f, 0f, 3.5f),
        Cyl("Lamp posts", "lamp post 4", Material.Metal, 19.5f, 9.5f, 0.15f, 0f, 3.5f),
        Cyl("Lamp posts", "lamp post 5", Material.Metal, 24f, 21.5f, 0.15f, 0f, 3.5f),
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
        Box("Hunter's lodge", "porch deck", Material.Wood, 18f, 20.5f, 4.5f, 9.5f, 0f, 0.2f, walkable: true),
        Box("Hunter's lodge", "porch step", Material.Wood, 17.5f, 18f, 4.5f, 9.5f, 0f, 0.1f, walkable: true),
        Cyl("Hunter's lodge", "post", Material.Wood, 18.25f, 4.75f, 0.12f, 0f, 3.2f),
        Cyl("Hunter's lodge", "post", Material.Wood, 18.25f, 9.25f, 0.12f, 0f, 3.2f),
        Box("Hunter's lodge", "porch roof", Material.Roof, 17.9f, 20.6f, 4.2f, 9.8f, 3.2f, 3.5f),
        Box("Cart", "bed", Material.Wood, 4f, 6.4f, 14f, 17.6f, 0.5f, 1.4f),
        Box("Cart", "wheel W", Material.Wood, 3.8f, 4f, 15.2f, 16.4f, 0f, 1.2f),
        Box("Cart", "wheel E", Material.Wood, 6.4f, 6.6f, 15.2f, 16.4f, 0f, 1.2f),
        Box("Cart", "shaft", Material.Wood, 4.9f, 5.5f, 17.6f, 19.6f, 0.6f, 0.8f),
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
        Box("Smithy", "body", Material.Stone, 6f, 14f, 2.8f, 8.5f, 0f, 4f),
        Gable("Smithy", "roof", Material.Roof, 5.7f, 14.3f, 2.5f, 8.8f, 4f, 5.8f),
        Cyl("Smithy", "chimney", Material.Stone, 12.5f, 4f, 0.5f, 0f, 7f),
        Cyl("Smithy", "post", Material.Wood, 7.2f, 10.8f, 0.12f, 0f, 3f),
        Cyl("Smithy", "post", Material.Wood, 10.8f, 10.8f, 0.12f, 0f, 3f),
        Box("Smithy", "lean-to roof", Material.Wood, 6.9f, 11.1f, 8.5f, 11.1f, 3f, 3.25f),
        Box("Smithy", "anvil", Material.Metal, 7.2f, 8f, 9.4f, 9.8f, 0f, 1.05f),
        Box("Smithy", "quench trough", Material.Wood, 12.8f, 14.3f, 8.6f, 9.2f, 0f, 1.05f),
        Box("Smithy", "wood pile", Material.Wood, 3.5f, 5.5f, 3f, 4f, 0f, 1.05f),
        Cyl("Barrels (smithy)", "barrel 1", Material.Wood, 15.5f, 4.5f, 0.4f, 0f, 1.05f),
        Cyl("Barrels (smithy)", "barrel 2", Material.Wood, 16.4f, 5.3f, 0.4f, 0f, 1.05f),
        Box("Armourer's stall", "counter", Material.Wood, 25f, 25.4f, 13.5f, 16.5f, 0f, 1.05f),
        Cyl("Armourer's stall", "post", Material.Wood, 25.2f, 13.6f, 0.1f, 0f, 2.9f),
        Cyl("Armourer's stall", "post", Material.Wood, 25.2f, 16.4f, 0.1f, 0f, 2.9f),
        Cyl("Armourer's stall", "post", Material.Wood, 27.3f, 13.6f, 0.1f, 0f, 2.9f),
        Cyl("Armourer's stall", "post", Material.Wood, 27.3f, 16.4f, 0.1f, 0f, 2.9f),
        Box("Armourer's stall", "roof", Material.Cloth, 24.6f, 27.6f, 13.1f, 16.9f, 2.9f, 3.15f),
        Box("Crates (armourer)", "crate 1", Material.Wood, 25f, 26f, 19.5f, 20.5f, 0f, 1.05f),
        Box("Crates (armourer)", "crate 2", Material.Wood, 26f, 27f, 19.5f, 20.5f, 0f, 1.05f),
        Box("General-goods stall", "counter", Material.Wood, 20f, 23f, 24f, 24.4f, 0f, 1.05f),
        Cyl("General-goods stall", "post", Material.Wood, 20.1f, 24.2f, 0.1f, 0f, 2.9f),
        Cyl("General-goods stall", "post", Material.Wood, 22.9f, 24.2f, 0.1f, 0f, 2.9f),
        Cyl("General-goods stall", "post", Material.Wood, 20.1f, 26.4f, 0.1f, 0f, 2.9f),
        Cyl("General-goods stall", "post", Material.Wood, 22.9f, 26.4f, 0.1f, 0f, 2.9f),
        Box("General-goods stall", "awning", Material.Cloth2, 19.4f, 23.6f, 23.2f, 27f, 2.9f, 3.2f),   // owner decision 2
        Box("Crates (general goods)", "crate 1", Material.Wood, 24.5f, 25.5f, 25f, 26f, 0f, 1.05f),
        Box("Crates (general goods)", "crate 2", Material.Wood, 25.5f, 26.5f, 25f, 26f, 0f, 1.05f),
        Cyl("Barrels (general goods)", "barrel", Material.Wood, 24.2f, 23.4f, 0.4f, 0f, 1.05f),
        Box("Benches", "bench market", Material.Wood, 5.1f, 6.9f, 23.75f, 24.25f, 0f, 1.05f),
        Cyl("Lamp posts", "lamp post 1", Material.Metal, 8f, 18.5f, 0.15f, 0f, 3.5f),
        Cyl("Lamp posts", "lamp post 2", Material.Metal, 22f, 20f, 0.15f, 0f, 3.5f),
    ];

    /// <summary>North-east: the bank with its steps, the inn with its porch, two houses, the well, crates and barrels.</summary>
    private static List<TownPiece> BankAndInn() =>
    [
        Box("Bank", "body", Material.Stone, 3f, 9f, 19f, 27f, 0f, 5f),
        Gable("Bank", "roof", Material.Roof, 2.7f, 9.3f, 18.7f, 27.3f, 5f, 6.5f),
        Box("Bank", "lower step", Material.Stone, 9f, 10.5f, 21f, 25f, 0f, 0.15f, walkable: true),
        Box("Bank", "upper step", Material.Stone, 9f, 9.75f, 21f, 25f, 0f, 0.3f, walkable: true),
        Box("Bank", "pilaster S", Material.Stone, 9f, 9.6f, 20.3f, 20.9f, 0f, 5f),
        Box("Bank", "pilaster N", Material.Stone, 9f, 9.6f, 25.1f, 25.7f, 0f, 5f),
        Box("Inn", "body", Material.Plaster, 14f, 26f, 21f, 27.25f, 0f, 7f),
        Gable("Inn", "roof", Material.Roof, 13.7f, 26.3f, 20.7f, 27.55f, 7f, 9.5f),
        Cyl("Inn", "chimney", Material.Stone, 24f, 24f, 0.45f, 0f, 10.5f),
        Box("Inn", "porch deck", Material.Wood, 16f, 24f, 18.5f, 21f, 0f, 0.2f, walkable: true),
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
