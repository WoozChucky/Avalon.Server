using Line = System.Collections.Generic.IReadOnlyList<(float X, float Z)>;

namespace Avalon.ChunkGen;

// The town ring's dressing (TownScenery's summary): a dresser a ring cell, the town ring's rules held before each item.
public static partial class TownScenery
{
    // ---- the scene: one dresser a cell ----

    /// <summary>
    /// The scene's dressing: a SceneryDresser per ring cell (its origin the cell's corner), each item sent to the cell
    /// holding its position and refused there when its turned kit box crosses the cell's edge. Before that, an item is
    /// held, in its cell's frame, to the town ring's height and edge-band rules (SceneryRules.AllowsTownItem, the rules
    /// CheckTownPiece holds every item to) and left out when they refuse it, so the scattering never builds a piece the
    /// rules refuse; a wrong model, kit, yaw or scale is thrown. Spacing is kept across cells, on a 4 m grid of the
    /// spots taken. The draws, spacing, clusters and fence runs are SceneryDresser's, over the scene's one random
    /// stream.
    /// </summary>
    private sealed class Scene
    {
        private readonly Dictionary<(int X, int Z), SceneryDresser> _cells = [];

        /// <summary>Each cell as an empty piece, for the rules to name and place an item in (SceneryRules.AllowsTownItem).</summary>
        private readonly Dictionary<(int X, int Z), SceneryPiece> _probes = [];
        private readonly IReadOnlyList<(int X, int Z)> _order;
        private readonly IReadOnlyDictionary<string, ModelBounds> _kit;
        private readonly Dictionary<(int X, int Z), List<(float X, float Z, float Radius)>> _taken = [];

        public Scene(SceneryTerrain terrain, Ground ground, IReadOnlyDictionary<string, ModelBounds> kit, IReadOnlyList<(int X, int Z)> cells)
        {
            Ground = ground;
            _kit = kit;
            _order = cells;
            foreach ((int x, int z) in cells)
            {
                _cells[(x, z)] = new SceneryDresser(SceneryComposer.CellName(Prefix, x, z), terrain, kit)
                {
                    Origin = (x * Cell, z * Cell),
                    FileKit = DecorJsonWriter.TownKit,
                };
                _probes[(x, z)] = new SceneryPiece(SceneryComposer.CellName(Prefix, x, z), terrain, []) { GridX = x, GridZ = z, Kit = DecorJsonWriter.TownKit };
            }
        }

        public Ground Ground { get; }

        public Random Rng { get; } = new(s_seed);

        /// <summary>Every cell's items, cell by cell in the ring's order.</summary>
        public IReadOnlyList<DecorItem> Items() => [.. _order.SelectMany(c => _cells[c].Items)];

        /// <summary>
        /// The model standing on the ground at (x, z), less the sink, from its kit (a nature model from the bright kit
        /// unless one is named), unless a rule or its cell refuses it.
        /// </summary>
        public bool Add(string model, float x, float z, int yaw, float scale, float sink = 0f, string? kit = null)
        {
            x = SceneryDresser.Round(x);
            z = SceneryDresser.Round(z);
            return AddAt(model, x, Ground.Height(x, z) - sink, z, yaw, scale, kit);
        }

        /// <summary><see cref="Add" />, at a scale lowered in steps until its box fits its cell and the rules (grass beside a seam).</summary>
        public bool AddShrinking(string model, float x, float z, int yaw, float scale, float sink = 0f)
        {
            for (; scale >= 0.3f; scale -= 0.12f)
            {
                if (Add(model, x, z, yaw, scale, sink)) return true;
            }
            return false;
        }

        public bool AddAt(string model, float x, float y, float z, int yaw, float scale, string? kit = null)
        {
            if (!_kit.TryGetValue(model, out ModelBounds? box))
                throw new InvalidOperationException($"{Prefix}: the kit's bounds table has no model '{model}'");
            // A nature model from the bright kit unless one is named; a town-kit model (a dark hedge) always from its own.
            kit = box.Kit == DecorJsonWriter.TownKit
                ? null
                : kit ?? (box.Kit == DecorJsonWriter.ForestKit && !model.StartsWith("avalon_", StringComparison.Ordinal) ? Bright : null);
            (int X, int Z) cell = ((int)MathF.Floor(x / Cell), (int)MathF.Floor(z / Cell));
            if (!_cells.TryGetValue(cell, out SceneryDresser? dresser)) return false;
            // The item as the dresser writes it, in its cell's frame as the composer moves it there.
            var item = new DecorItem("", model, SceneryDresser.Round(x) - cell.X * Cell, SceneryDresser.Round(y), SceneryDresser.Round(z) - cell.Z * Cell,
                Yaw.Normal(yaw), SceneryDresser.Round(scale), kit ?? (box.Kit == DecorJsonWriter.TownKit ? null : box.Kit));
            return SceneryRules.AllowsTownItem(_probes[cell], item, _kit) && dresser.TryAddAt(model, x, y, z, yaw, scale, kit);
        }

        /// <summary>The model at an open spot of the radius, which it then takes.</summary>
        public bool Place(string model, float x, float z, float radius, int yaw, float scale, float sink, string? kit = null)
        {
            if (!Open(x, z, radius) || !Add(model, x, z, yaw, scale, sink, kit)) return false;
            Take(x, z, radius);
            return true;
        }

        /// <summary>
        /// A kit tree of the species (one of <paramref name="models" />, else one of its five) at an open spot, from the kit
        /// given, its trunk's spot taken with <paramref name="clearance" /> round it (less in a clump). Near a cell's
        /// edge a crown's box, turned, may cross it at one yaw and not at another: each of a few tries draws the model
        /// and the yaw again.
        /// </summary>
        public bool Tree(TreeSpecies species, float x, float z, float scale, string kit, IReadOnlyList<string>? models = null, float clearance = 1f)
        {
            float radius = ForestDresser.TrunkRadius(species) * scale + clearance;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                string model = models is null ? ForestDresser.TreeModel(species, Rng) : Pick(models);
                if (Place(model, x, z, radius, Rng.Next(360), scale, 0.1f, kit)) return true;
            }
            return false;
        }

        /// <summary>A loose cluster of the models about a point, each at a scale in the range, within <paramref name="spread" />.</summary>
        public void Cluster(float x, float z, string[] models, float spread, float minScale, float maxScale) =>
            SceneryDresser.Cluster(Rng, x, z, models, spread, minScale, maxScale, attempts: 8,
                (model, px, pz, scale) => Place(model, px, pz, Spacing(model, scale, 0.35f), Rng.Next(360), scale, 0.04f));

        /// <summary>
        /// Wooden fence sections from one corner to the next along x or z (SceneryDresser.FenceSections), each a few
        /// degrees off square and scaled to its length; now and then one left out (<paramref name="gaps" />).
        /// </summary>
        public void FenceRun((float X, float Z) from, (float X, float Z) to, float gaps = 0f)
        {
            foreach ((string model, float fx, float fz, int yaw, float length) in SceneryDresser.FenceSections(from, to))
            {
                if (gaps > 0f && Rng.NextDouble() < gaps) continue;
                if (Add(model, fx, fz, yaw + Rng.Next(-2, 3), length / 2.04f)) Take(fx, fz, 0.45f);
            }
        }

        /// <summary>
        /// A hedgerow along the line: green hedges and bushes overlapping, a flowering one now and then, a tree here and
        /// there; <paramref name="gap" /> of its places left open.
        /// </summary>
        public void Hedge(Line line, float gap, float height = 1f)
        {
            for (int s = 1; s < line.Count; s++)
            {
                (float ax, float az) = line[s - 1];
                (float bx, float bz) = line[s];
                float length = SceneryDresser.Distance(ax, az, bx, bz);
                for (float t = 0f; t <= length; t += Uniform(1.1f, 1.5f))
                {
                    if (Rng.NextDouble() < gap) continue;
                    float x = ax + (bx - ax) * t / Math.Max(length, 0.01f) + Uniform(-0.25f, 0.25f), z = az + (bz - az) * t / Math.Max(length, 0.01f) + Uniform(-0.25f, 0.25f);
                    string model = PickWeighted([("avalon_hedge_green", 7), ("avalon_hedge_dark", 2), ("Bush_Common_Flowers", 1)]);
                    for (float scale = Uniform(0.85f, 1.15f) * height; scale > 0.55f; scale -= 0.1f)
                    {
                        if (Add(model, x, z, Rng.Next(360), scale, 0.08f)) break;
                    }
                    if (Rng.NextDouble() < 0.1) Tree(TreeSpecies.CommonTree, x, z, Uniform(0.8f, 1.1f), Bright);
                }
            }
        }

        /// <summary>Whether a spot of the radius is clear of every spot taken so far and of every building's footprint.</summary>
        public bool Open(float x, float z, float radius)
        {
            if (Ground.NearBuilding(x, z, radius)) return false;
            (int gx, int gz) = Grid(x, z);
            for (int i = gx - 2; i <= gx + 2; i++)
            {
                for (int j = gz - 2; j <= gz + 2; j++)
                {
                    if (_taken.TryGetValue((i, j), out List<(float X, float Z, float Radius)>? spots)
                        && spots.Any(s => SceneryDresser.Distance(x, z, s.X, s.Z) < s.Radius + radius))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>Takes a spot (a radius of at most 8 m is seen by the grid's neighbours).</summary>
        public void Take(float x, float z, float radius)
        {
            (int gx, int gz) = Grid(x, z);
            if (!_taken.TryGetValue((gx, gz), out List<(float X, float Z, float Radius)>? spots)) _taken[(gx, gz)] = spots = [];
            spots.Add((x, z, Math.Min(radius, 8f)));
        }

        /// <summary>Removes every item standing within the radius (a scarecrow's place in the wheat).</summary>
        public void Remove(float x, float z, float radius)
        {
            foreach (SceneryDresser dresser in _cells.Values)
                dresser.Items.RemoveAll(i => SceneryDresser.Distance(i.X, i.Z, x, z) < radius);
        }

        public float Spacing(string model, float scale, float spread) => SceneryDresser.Spacing(_kit[model], scale, spread);

        public float Uniform(float min, float max) => SceneryDresser.Uniform(Rng, min, max);

        public string Pick(IReadOnlyList<string> models) => SceneryDresser.Pick(Rng, models);

        public string PickWeighted(IReadOnlyList<(string Model, int Weight)> models) => SceneryDresser.PickWeighted(Rng, models);

        private static (int X, int Z) Grid(float x, float z) => ((int)MathF.Floor(x / 4f), (int)MathF.Floor(z / 4f));
    }
}
