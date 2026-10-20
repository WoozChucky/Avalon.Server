using Avalon.World.ChunkLayouts;

namespace Avalon.Server.World.UnitTests.Procedural;

/// <summary>
/// A minimal complete forest scenery pool, as a ring file holds it: three filler borders and two others, a cap of every
/// shape (two for one capped side) and the 8 town-ward spots. A test fixture for the ring generator.
/// </summary>
internal static class SceneryTestPool
{
    public const string Json = """
        { "mapTemplateId": 2, "pool": [
          { "name": "t_woods_1", "kind": "border", "weight": 3, "front": "S", "filler": true },
          { "name": "t_woods_2", "kind": "border", "weight": 3, "front": "S", "filler": true },
          { "name": "t_woods_3", "kind": "border", "weight": 3, "front": "S", "filler": true },
          { "name": "t_cliffs", "kind": "border", "weight": 1, "front": "S", "filler": false },
          { "name": "t_ravine", "kind": "border", "weight": 1, "front": "S", "filler": false },
          { "name": "t_cap_s", "kind": "cap", "weight": 1, "capped": ["S"] },
          { "name": "t_cap_s2", "kind": "cap", "weight": 1, "capped": ["S"] },
          { "name": "t_cap_sw", "kind": "cap", "weight": 1, "capped": ["S", "W"] },
          { "name": "t_cap_ns", "kind": "cap", "weight": 1, "capped": ["N", "S"] },
          { "name": "t_cap_nsw", "kind": "cap", "weight": 1, "capped": ["N", "S", "W"] },
          { "name": "t_cap_all", "kind": "cap", "weight": 1, "capped": ["N", "E", "S", "W"] },
          { "name": "t_tw_m1_m1", "kind": "townward", "spot": [-1, -1] },
          { "name": "t_tw_0_m1", "kind": "townward", "spot": [0, -1] },
          { "name": "t_tw_1_m1", "kind": "townward", "spot": [1, -1] },
          { "name": "t_tw_m1_0", "kind": "townward", "spot": [-1, 0] },
          { "name": "t_tw_1_0", "kind": "townward", "spot": [1, 0] },
          { "name": "t_tw_m1_1", "kind": "townward", "spot": [-1, 1] },
          { "name": "t_tw_0_1", "kind": "townward", "spot": [0, 1] },
          { "name": "t_tw_1_1", "kind": "townward", "spot": [1, 1] }
        ] }
        """;

    public static SceneryPool Pool { get; } = SceneryFile.Read(Json, nameof(SceneryTestPool)).Pool!;

    /// <summary>The same pool without its town-ward pieces.</summary>
    public static SceneryPool WithoutTownWard { get; } = new(Pool.Pieces.Where(p => p.Kind != SceneryKind.TownWard).ToList());
}
