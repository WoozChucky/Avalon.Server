using Avalon.ChunkGen;
using Avalon.World.ChunkLayouts;

namespace Avalon.Server.World.UnitTests.Procedural;

/// <summary>
/// Scenery pieces that pass as ChunkGen would write them, for tests that break one thing. <see cref="Forest" /> is a
/// minimal forest pool: three old-woods fillers, one border of every other family, the four single caps (capped S), a
/// cap of every other shape and the 8 town-ward spots, each a flat forest-floor cell with ferns every 2 m 1.5 m in along
/// every edge and, outside a cap, one tree in the middle. <see cref="Town" /> is the town's 21 ring cells, flat meadow,
/// cut from one scene. Fixtures, not tests.
/// </summary>
internal static class SceneryTestPieces
{
    public static IReadOnlyList<SceneryPiece> Forest()
    {
        List<SceneryPiece> pieces =
        [
            Border("forest_ring_woods_01", "woods", 3f, filler: true),
            Border("forest_ring_woods_02", "woods", 3f, filler: true),
            Border("forest_ring_woods_03", "woods", 3f, filler: true),
            Border("forest_ring_cliffs_01", "cliffs", 1f),
            Border("forest_ring_ravine_01", "ravine", 1f),
            Border("forest_ring_giant_01", "giant", 0.75f),
            Border("forest_ring_stream_01", "stream", 1f),
            Cap("forest_cap_rockfall_01", "rockfall", Side.S),
            Cap("forest_cap_trunk_01", "trunk", Side.S),
            Cap("forest_cap_thicket_01", "thicket", Side.S),
            Cap("forest_cap_brink_01", "brink", Side.S),
            Cap("forest_cap_corner_01", "cap", Side.S, Side.W),
            Cap("forest_cap_opposite_01", "cap", Side.N, Side.S),
            Cap("forest_cap_three_01", "cap", Side.N, Side.S, Side.W),
            Cap("forest_cap_four_01", "cap", Side.N, Side.E, Side.S, Side.W),
        ];
        foreach (int dx in new[] { -1, 0, 1 })
        {
            foreach (int dz in new[] { -1, 0, 1 })
            {
                if ((dx, dz) == (0, 0)) continue;
                pieces.Add(new SceneryPiece(SceneryComposer.CellName("forest_townward", dx, dz), Flat("forest_floor"), [.. Ferns(), Tree])
                {
                    Kind = SceneryKind.TownWard,
                    Family = "townward",
                    Spot = (dx, dz),
                    Kit = DecorJsonWriter.ForestKit,
                });
            }
        }
        return pieces;
    }

    /// <summary>The town's cells, as TownPieces lays its squares out.</summary>
    public static IReadOnlyList<(int X, int Z)> TownCells { get; } = [.. TownPieces.Squares().Select(s => (s.GridX, s.GridZ))];

    /// <summary>
    /// The town's 21 ring cells, x and z -1 to 3 minus the 2 x 2 town, bare, cut from <paramref name="world" /> (world
    /// metres; flat meadow when null).
    /// </summary>
    public static IReadOnlyList<SceneryPiece> Town(SceneryTerrain? world = null) =>
        SceneryComposer.Cut("town_ring", SceneryRing.Cells(TownCells), world ?? Flat("meadow"), [], DecorJsonWriter.TownKit,
            (_, piece) => piece with { Family = "town" });

    public static SceneryTerrain Flat(string material) => new((_, _) => 0f, (_, _) => material);

    /// <summary>A fern every 2 m (2 to 28 m along), 1.5 m in from each edge: the barrier band's minimum, each inside the cell.</summary>
    public static IEnumerable<DecorItem> Ferns()
    {
        for (float along = 2f; along < ChunkPiece.CellSize - 1f; along += 2f)
        {
            yield return Fern(along, 1.5f);
            yield return Fern(along, ChunkPiece.CellSize - 1.5f);
            yield return Fern(1.5f, along);
            yield return Fern(ChunkPiece.CellSize - 1.5f, along);
        }
    }

    private static DecorItem Tree { get; } = new("", "CommonTree_1", 15f, 0f, 15f, 0);

    private static DecorItem Fern(float x, float z) => new("", "Fern_1", x, 0f, z, 0);

    private static SceneryPiece Border(string name, string family, float weight, bool filler = false) =>
        new(name, Flat("forest_floor"), [.. Ferns(), Tree])
        {
            Kind = SceneryKind.Border,
            Family = family,
            Weight = weight,
            Front = Side.S,
            Filler = filler,
            Kit = DecorJsonWriter.ForestKit,
        };

    private static SceneryPiece Cap(string name, string family, params Side[] capped) =>
        new(name, Flat("forest_floor"), [.. Ferns()])
        {
            Kind = SceneryKind.Cap,
            Family = family,
            Weight = 1f,
            Capped = capped,
            Kit = DecorJsonWriter.ForestKit,
        };
}
