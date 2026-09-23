using System.Globalization;
using System.Reflection;
using System.Text;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Maps.Navigation;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Exporter;

/// <summary>
/// Exports what the server's own navmesh answers the two queries it steps movement with. Every row
/// below is produced by running the real <see cref="ChunkLayoutNavmeshBuilder"/> over the real chunk
/// .objs and querying the real <see cref="MapNavigator"/> -- never a copy of either, because a copy
/// agrees with whatever it copied and that is the failure these vectors exist to catch.
///
/// The vectors are keyed on a LAYOUT rather than on a composed .obj, so one row covers composition,
/// bake and query end to end. A vector keyed on pre-composed geometry would skip the composition
/// step, which is its own mirrored arithmetic.
/// </summary>
public static class NavmeshVectors
{
    public const string FileName = "navmesh-v1.txt";

    /// <summary>Where the chunk .objs live, relative to the repository root.</summary>
    private const string ChunkContentRoot = "src/Server/Avalon.Server.World";

    public static void Write(string outputPath)
    {
        var text = new StringBuilder();
        text.Append(Header);

        var rays = 0;
        var grounds = 0;

        foreach (LayoutSpec spec in Layouts)
        {
            Emit(text, spec, ref rays, ref grounds);
        }

        Lf.Write(outputPath, text.ToString());

        Console.WriteLine($"wrote {outputPath} ({Layouts.Count} layouts; {rays} rays, {grounds} ground samples)");
    }

    // ---------------------------------------------------------------- the layouts and their queries

    private sealed record ChunkSpec(string Template, short GridX, short GridZ, byte Rotation);

    private sealed record RaySpec(Vector3 From, Vector3 To, string Label);

    private sealed record GroundSpec(float X, float Y, float Z, string Label);

    private sealed record LayoutSpec(
        string Name,
        float CellSize,
        IReadOnlyList<RaySpec> Rays,
        IReadOnlyList<GroundSpec> Grounds,
        IReadOnlyList<ChunkSpec> Chunks);

    /// <summary>Where a town floor's navmesh sits: the slab's top face is at local y = 0.05.</summary>
    private const float TownFloorY = 0.15f;

    /// <summary>The default sample height: above the floor, and inside the pick extent's reach.</summary>
    private const float SampleFromY = 3f;

    private static RaySpec Ray(float fx, float fz, float tx, float tz, string label, float y = TownFloorY)
        => new(new Vector3(fx, y, fz), new Vector3(tx, y, tz), label);

    private static GroundSpec Ground(float x, float z, string label, float y = SampleFromY)
        => new(x, y, z, label);

    private static readonly IReadOnlyList<LayoutSpec> Layouts =
    [
        // The live town: four chunks, unrotated, walled apart along x = 30 and z = 30 with doorways
        // at 12..18 and 42..48. The one layout a player has actually stood in.
        new("town", 30f,
            [
                Ray(15f, 15f, 16f, 15f, "open floor, one metre"),
                Ray(15f, 15f, 25f, 15f, "open floor, the length of a tile and then some"),
                Ray(45f, 45f, 35f, 35f, "open floor, diagonal, north-east quadrant"),

                // Tile world size is 32 * 0.3 = 9.6, and the grid starts at the geometry's own
                // minimum, so the seams fall at -0.25 + k * 9.6: 9.35, 18.95, 28.55. Agent values
                // handed to a tile header in VOXELS rather than world units are observable only
                // across a seam, so these cross one, two and several of them.
                Ray(5f, 5f, 15f, 5f, "crosses the x = 9.35 tile seam"),
                Ray(5f, 5f, 25f, 5f, "crosses the x = 9.35 and 18.95 tile seams"),
                Ray(5f, 14f, 25f, 14f, "crosses two seams a metre below the z = 14.35 one"),
                Ray(14f, 5f, 14f, 25f, "the same, on z -- a transposed tile index lands elsewhere"),

                // A SHALLOW diagonal, not a 45-degree one. A ray along x = z from an integer start
                // passes exactly through the point where four tiles meet, and Detour cannot step a
                // raycast through a vertex: it clamps there, on a knife edge that two Recast
                // implementations may fall on either side of. This crosses every seam on both axes
                // without ever crossing a tile corner.
                Ray(2f, 3f, 27f, 28f, "shallow diagonal across the seams on both axes"),

                // Into walls, from several angles and distances. The navmesh is eroded by the agent
                // radius before a polygon reaches a wall, so these clamp short of the wall's face.
                Ray(25f, 25f, 35f, 25f, "head-on into the x = 30 wall"),
                Ray(20f, 25f, 40f, 25f, "the same wall from twice the distance"),
                // 45 degrees at the corner where both walls meet -- and it stops at the TILE corner
                // (28.55) rather than at either wall's eroded face (28.85, where the head-on row
                // above stops). The barrier is a polygon edge on the tile seam, not the wall.
                Ray(25f, 25f, 35f, 35f, "45 degrees, clamping on the tile corner short of the walls"),
                Ray(25f, 5f, 25f, 35f, "north into the z = 30 wall"),
                Ray(28.9f, 25f, 35f, 25f, "flush against the x = 30 wall, pointing into it"),
                Ray(25f, 28.9f, 25f, 5f, "flush against the z = 30 wall, pointing away from it"),

                // The doorways: 12..18 and 42..48 are open, so these cross a chunk seam and arrive.
                Ray(15f, 25f, 15f, 35f, "through the z = 30 doorway at x = 15"),
                Ray(25f, 45f, 35f, 45f, "through the x = 30 doorway at z = 45"),
                Ray(15f, 25f, 45f, 25f, "at the doorways' own centre line but across a wall"),

                Ray(9000f, 9000f, 9001f, 9000f, "starts off the mesh entirely", y: 0f),
                Ray(-5f, 15f, 15f, 15f, "starts outside the town's west edge", y: 0f),
            ],
            [
                Ground(15f, 15f, "open floor, south-west quadrant"),
                Ground(45f, 45f, "open floor, north-east quadrant"),
                Ground(9.35f, 9.35f, "on the tile corner at 9.35"),
                Ground(28f, 25f, "two metres from the x = 30 wall"),
                Ground(15f, 30f, "in the z = 30 doorway"),
                Ground(30f, 25f, "inside the x = 30 wall itself -- off the mesh"),
                Ground(15f, 15f, "open floor, sampled from five metres BELOW it", y: -5f),
                Ground(15f, 15f, "open floor, sampled from beyond the pick extent's reach", y: 40f),
                Ground(9000f, 9000f, "off the mesh entirely"),
            ],
            [
                new("town_sw_01", 0, 0, 0),
                new("town_se_01", 1, 0, 0),
                new("town_nw_01", 0, 1, 0),
                new("town_ne_01", 1, 1, 0),
            ]),

        // Every rotation value, on a chunk that is not symmetric under any of them: town_corner_01
        // carries a wall on its north and west sides and nothing on the other two. A transposed
        // rotation case draws a map that looks like a map; only these rows disagree.
        new("rotations", 30f,
            [
                // Each ray runs toward one edge of its cell. Rotation 0 walls the north and the
                // west; 1 the north and the east; 2 the south and the east; 3 the south and the
                // west. An edge with no wall still clamps when it is the mesh's own boundary -- but
                // a little nearer, because a wall pushes the eroded polygon a further 0.3 back, and
                // that difference is what tells a wall from an edge here.
                Ray(15f, 15f, 15f, 32f, "r0 cell, north"),
                Ray(15f, 15f, -2f, 15f, "r0 cell, west"),
                Ray(15f, 15f, 32f, 15f, "r0 cell, east"),
                Ray(15f, 15f, 15f, -2f, "r0 cell, south"),

                Ray(45f, 15f, 45f, 32f, "r1 cell, north"),
                Ray(45f, 15f, 28f, 15f, "r1 cell, west"),
                Ray(45f, 15f, 62f, 15f, "r1 cell, east"),
                Ray(45f, 15f, 45f, -2f, "r1 cell, south"),

                Ray(15f, 45f, 15f, 62f, "r2 cell, north"),
                Ray(15f, 45f, -2f, 45f, "r2 cell, west"),
                Ray(15f, 45f, 32f, 45f, "r2 cell, east"),
                Ray(15f, 45f, 15f, 28f, "r2 cell, south"),

                Ray(45f, 45f, 45f, 62f, "r3 cell, north"),
                Ray(45f, 45f, 28f, 45f, "r3 cell, west"),
                Ray(45f, 45f, 62f, 45f, "r3 cell, east"),
                Ray(45f, 45f, 45f, 28f, "r3 cell, south"),
            ],
            [
                Ground(15f, 15f, "r0 cell centre"),
                Ground(45f, 15f, "r1 cell centre"),
                Ground(15f, 45f, "r2 cell centre"),
                Ground(45f, 45f, "r3 cell centre"),
            ],
            [
                new("town_corner_01", 0, 0, 0),
                new("town_corner_01", 1, 0, 1),
                new("town_corner_01", 0, 1, 2),
                new("town_corner_01", 1, 1, 3),
            ]),

        // Four unwalled forest chunks, so the floors join and a ray crosses a chunk seam on open
        // ground. Two have their floor's top face at local y = 0 and two at 0.05, which is the
        // LARGEST vertical variation a layout composed from real chunks can produce -- and the
        // ground rows below are the bake quantising both to one height. See the header.
        new("forest-floors", 30f,
            [
                Ray(15f, 15f, 45f, 15f, "across the seam between two chunks of equal floor height",
                    y: 0.1f),
                Ray(15f, 15f, 15f, 45f, "across the seam between the two floor heights", y: 0.1f),
                Ray(15f, 15f, 45f, 45f, "diagonally across both seams", y: 0.1f),
                Ray(15f, 15f, 15f, -5f, "off the layout's south edge", y: 0.1f),
            ],
            [
                Ground(15f, 15f, "forest_entry_01, floor top at local y = 0"),
                Ground(45f, 15f, "forest_path_01, floor top at local y = 0"),
                Ground(15f, 45f, "forest_clearing_01, floor top at local y = 0.05"),
                Ground(45f, 45f, "forest_path_03, floor top at local y = 0.05"),
                Ground(15f, 30f, "on the seam between the two heights"),
            ],
            [
                new("forest_entry_01", 0, 0, 0),
                new("forest_path_01", 1, 0, 0),
                new("forest_clearing_01", 0, 1, 0),
                new("forest_path_03", 1, 1, 0),
            ]),
    ];

    // ---------------------------------------------------------------- running the server's own code

    private static void Emit(StringBuilder text, LayoutSpec spec, ref int rays, ref int grounds)
    {
        ChunkLayout layout = BuildLayout(spec);
        DtNavMesh mesh = Bake(layout, spec);

        var navigator = new MapNavigator(NullLoggerFactory.Instance);
        navigator.LoadFromNavMesh(mesh);

        var reader = new RaycastReader(mesh);

        (int tiles, int polys, int verts) = Counts(mesh);

        text.Append('\n');
        text.Append(CultureInfo.InvariantCulture, $"layout {spec.Name} cell {F(spec.CellSize)}\n");

        foreach (ChunkSpec chunk in spec.Chunks)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"chunk {chunk.Template} {chunk.GridX} {chunk.GridZ} {chunk.Rotation}\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"mesh tiles {tiles} polys {polys} verts {verts}\n");

        foreach (RaySpec ray in spec.Rays)
        {
            (string outcome, float t) = reader.Classify(ray.From, ray.To);
            Vector3 position = navigator.RaycastWalkable(ray.From, ray.To);

            // THE SERVER'S OWN METHOD IS THE ORACLE for the position, and the reconstruction above
            // is held to it on every row. The branch and t cannot be read off RaycastWalkable --
            // it returns a bare Vector3 and collapses two of its three branches onto the same one --
            // so they are recomputed from the same mesh, through the same Detour calls, using the
            // navigator's own constants read off the type by reflection. If that recomputation and
            // the real method ever disagree about where the ray ended, the export fails rather than
            // writing a number no server produced.
            Vector3 rebuilt = Clamp(ray.From, ray.To, outcome, t);
            if (rebuilt.x != position.x || rebuilt.y != position.y || rebuilt.z != position.z)
            {
                throw new InvalidOperationException(
                    $"navmesh vectors: layout '{spec.Name}' ray '{ray.Label}' -- the reconstruction " +
                    $"({F(rebuilt.x)}, {F(rebuilt.y)}, {F(rebuilt.z)}) disagrees with MapNavigator " +
                    $"({F(position.x)}, {F(position.y)}, {F(position.z)}). The mirror of RaycastWalkable " +
                    "in this file no longer matches the one in MapNavigator.");
            }

            text.Append(CultureInfo.InvariantCulture, $"ray {F(ray.From.x)} {F(ray.From.y)} {F(ray.From.z)} ");
            text.Append(CultureInfo.InvariantCulture, $"{F(ray.To.x)} {F(ray.To.y)} {F(ray.To.z)} -> ");
            text.Append(CultureInfo.InvariantCulture,
                $"{outcome} {F(position.x)} {F(position.y)} {F(position.z)} {F(t)} # {ray.Label}\n");
            ++rays;
        }

        foreach (GroundSpec ground in spec.Grounds)
        {
            float height = navigator.SampleGroundHeight(ground.X, ground.Y, ground.Z);
            text.Append(CultureInfo.InvariantCulture,
                $"ground {F(ground.X)} {F(ground.Y)} {F(ground.Z)} -> {F(height)} # {ground.Label}\n");
            ++grounds;
        }
    }

    private static Vector3 Clamp(Vector3 from, Vector3 to, string outcome, float t) => outcome switch
    {
        "reached" => to,
        "nostartpoly" => from,
        _ => new Vector3(from.x + (to.x - from.x) * t,
                         from.y + (to.y - from.y) * t,
                         from.z + (to.z - from.z) * t),
    };

    private static ChunkLayout BuildLayout(LayoutSpec spec)
    {
        var placed = spec.Chunks
            .Select((chunk, index) => new PlacedChunk(
                new ChunkTemplateId(index + 1), chunk.GridX, chunk.GridZ, chunk.Rotation,
                new Vector3(chunk.GridX * spec.CellSize, 0, chunk.GridZ * spec.CellSize)))
            .ToList();

        return new ChunkLayout(
            Seed: 0,
            Chunks: placed,
            EntryChunk: placed[0],
            BossChunk: null,
            Portals: [],
            EntrySpawnWorldPos: Vector3.zero,
            CellSize: spec.CellSize);
    }

    /// <summary>
    /// The real builder, over the real .objs. It resolves them relative to the process working
    /// directory, which is where the server's own content root sits at run time -- so the export
    /// stands in that directory for the length of the bake rather than reimplementing the lookup.
    /// </summary>
    private static DtNavMesh Bake(ChunkLayout layout, LayoutSpec spec)
    {
        var library = new NameOnlyChunkLibrary([.. spec.Chunks.Select(c => c.Template)], spec.CellSize);
        var builder = new ChunkLayoutNavmeshBuilder(NullLoggerFactory.Instance, library);

        string previous = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(Path.Combine(RepositoryRoot.Find(), ChunkContentRoot));
        try
        {
            return builder.BuildAsync(layout, CancellationToken.None).GetAwaiter().GetResult();
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }

    private static (int Tiles, int Polys, int Verts) Counts(DtNavMesh mesh)
    {
        var tiles = 0;
        var polys = 0;
        var verts = 0;

        for (var i = 0; i < mesh.GetMaxTiles(); ++i)
        {
            DtMeshTile tile = mesh.GetTile(i);
            if (tile?.data?.header is null) continue;
            ++tiles;
            polys += tile.data.header.polyCount;
            verts += tile.data.header.vertCount;
        }

        return (tiles, polys, verts);
    }

    private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>
    /// The branch <see cref="MapNavigator.RaycastWalkable"/> took, and the raw fraction it took it
    /// on. Both are recovered by running the same two Detour calls against the same mesh with the
    /// navigator's OWN constants, read off the type rather than re-typed here: a renamed field is a
    /// loud failure at export time, and a changed value arrives in the vectors as a diff.
    /// </summary>
    private sealed class RaycastReader
    {
        private readonly DtNavMeshQuery _query;
        private readonly IDtQueryFilter _filter = new DtQueryDefaultFilter();
        private readonly RcVec3f _pickExt;
        private readonly int _maxPolys;

        internal RaycastReader(DtNavMesh mesh)
        {
            _query = new DtNavMeshQuery(mesh);
            _pickExt = (RcVec3f)Private("PolyPickExt");
            _maxPolys = (int)Private("MaxPolys");
        }

        private static object Private(string name)
        {
            FieldInfo field = typeof(MapNavigator).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException(
                    $"MapNavigator no longer has a private static '{name}'. The vectors read the " +
                    "navigator's own query constants rather than copying them; find where it moved to.");

            return field.GetValue(null)
                ?? throw new InvalidOperationException($"MapNavigator.{name} is null.");
        }

        internal (string Outcome, float T) Classify(Vector3 from, Vector3 to)
        {
            var start = new RcVec3f(from.x, from.y, from.z);
            DtStatus nearest = _query.FindNearestPoly(start, _pickExt, _filter, out long startRef, out _, out _);
            if (nearest.Failed() || startRef == 0) return ("nostartpoly", 0f);

            var end = new RcVec3f(to.x, to.y, to.z);
            long[] path = new long[_maxPolys];
            DtStatus cast = _query.Raycast(startRef, start, end, _filter, out float t, out _, path, out _, _maxPolys);
            if (cast.Failed()) return ("nostartpoly", 0f);

            // Detour's "nothing was hit" is a sentinel, not a large number: the ray ended inside a
            // polygon, so there is no fraction and t is float.MaxValue. It is emitted as it stands.
            return t >= float.MaxValue ? ("reached", t) : ("clamped", t);
        }
    }

    /// <summary>
    /// The only thing the bake asks a chunk library for is the template's name, which is what the
    /// .obj on disk is called. The production library reads it from the world database; nothing in
    /// an export should need one running, so this answers that one question and refuses the rest.
    /// </summary>
    private sealed class NameOnlyChunkLibrary : IChunkLibrary
    {
        private readonly IReadOnlyList<string> _names;
        private readonly float _cellSize;

        internal NameOnlyChunkLibrary(IReadOnlyList<string> names, float cellSize)
        {
            _names = names;
            _cellSize = cellSize;
        }

        public Task LoadAsync(CancellationToken ct) => Task.CompletedTask;

        public ChunkTemplate GetById(ChunkTemplateId id) => new()
        {
            Id = id,
            Name = _names[id.Value - 1],
            CellSize = _cellSize,
        };

        public IReadOnlyList<ChunkPoolMember> GetByPool(ChunkPoolId poolId) =>
            throw new NotSupportedException("The navmesh export places chunks itself; it selects none.");

        public IReadOnlyDictionary<ChunkTemplateId, ChunkTemplate> LookupByIds(IEnumerable<ChunkTemplateId> ids) =>
            ids.ToDictionary(id => id, GetById);
    }

    private const string Header = """
        # Navmesh - known-answer vectors for the two queries the server steps movement with.
        #
        # GENERATED FILE. Every answer below is what the server's own DotRecast bake and its own
        # MapNavigator produced for the layout it sits under, so a client is conformant when it
        # reproduces them, and a change in how the server bakes or queries arrives here as a diff.
        #
        #   Emitted by  tools/Avalon.Exporter  (Avalon.Server)
        #   Regenerate  dotnet run --project tools/Avalon.Exporter -- navmesh
        #   Source      src/Server/Avalon.World/ChunkLayouts/IChunkLayoutNavmeshBuilder.cs
        #               src/Server/Avalon.World/Maps/Navigation/NavmeshBuildSettings.cs
        #               src/Server/Avalon.World/Maps/Navigation/MapNavigator.cs
        #               src/Server/Avalon.Server.World/Maps/Chunks/*.obj
        #
        # A CLIENT THAT BAKES A DIFFERENT NAVMESH DOES NOT FAIL. It walks through a wall the server
        # stops it at, and the player sees a correction rather than an error; nothing at run time
        # reports the disagreement. Only these numbers do.
        #
        # KEYED ON A LAYOUT, NOT ON GEOMETRY. The client composes the combined mesh itself from the
        # chunk names, grid cells and rotations below, so one row covers composition, bake and query
        # end to end. A vector keyed on a pre-composed .obj would skip the composition step, which is
        # its own mirrored arithmetic and its own way to be wrong.
        #
        # Format, one block per layout:
        #
        #   layout <name> cell <cellSize>
        #   chunk  <template> <gridX> <gridZ> <rotation>       (repeated; origin = grid * cellSize)
        #   mesh   tiles <n> polys <n> verts <n>
        #   ray    <fx> <fy> <fz> <tx> <ty> <tz> -> <outcome> <px> <py> <pz> <t>   # label
        #   ground <x> <y> <z> -> <height>                                         # label
        #
        # <outcome> IS THE BRANCH, NOT AN INFERENCE FROM THE POSITION. RaycastWalkable returns a bare
        # position and collapses three branches onto it -- no start polygon hands back `from`, a
        # failed raycast hands back `from`, and a clamp at t = 0 lands on `from` as well -- so a
        # reader cannot tell "you did not move" from "you are not on the mesh". The three are:
        #
        #   reached      the ray ended inside a polygon; position is the destination
        #   clamped      the ray left the mesh at fraction t; position is from + (to - from) * t
        #   nostartpoly  no polygon under `from`, or Detour refused the cast; position is `from`
        #
        # <t> IS DETOUR'S RAW VALUE, sentinel and all: a ray that reaches its destination reports
        # float.MaxValue (3.4028235E+38), because there is no fraction to report. Treat it as a
        # sentinel; do not epsilon-compare it.
        #
        # The mesh counts are the bake's structure, and they are here because a bake landing on
        # different counts has already diverged -- there is nothing further worth comparing. They are
        # tile, polygon and vertex totals over every loaded tile. A BV-tree node count is NOT here:
        # it differs between implementations even when the navmesh is identical.
        #
        # HEIGHT COVERAGE IS BOUNDED BY THE CHUNK LIBRARY, and that is a real gap rather than an
        # oversight. Every chunk .obj the server ships is a flat slab: a town floor's top face is at
        # local y = 0.05, a forest floor's at 0 or 0.05, and the walls are vertical, so nothing above
        # a floor is walkable at all. The 'forest-floors' layout puts the two floor heights beside
        # each other -- the largest vertical variation a layout composed from real chunks can produce
        # -- and its ground rows all come back at one height, because 5 cm is a quarter of the bake's
        # 0.2 cell height and both floors quantise to the same voxel. So these vectors pin
        # SampleGroundHeight at a single height.
        #
        # THAT FLATNESS HAS A SECOND CONSEQUENCE, and it is the one easier to miss: the agent values
        # written into each TILE HEADER are unpinned by anything here. Detour reads walkableHeight
        # nowhere outside the endian swap, reads walkableRadius only under DT_FINDPATH_ANY_ANGLE,
        # which neither of these queries takes, and reads walkableClimb only where two surfaces
        # differ in height -- a portal-edge overlap test that links coplanar edges at any tolerance,
        # and a tie-break that needs two walkable polygons stacked under one column. Passing those
        # values in voxels rather than world units makes the climb LARGER, so it can only add a link,
        # and adding one takes a step of roughly 0.9 to 4 metres falling on a tile seam. No layout
        # composed from this chunk library has one. It is not a thinner query set that would catch
        # it: it is uncatchable from a layout, until the library gains real relief.
        #
        # A RAY CAN CLAMP ON A TILE CORNER RATHER THAN ON THE GEOMETRY, and one row here does. Tiles
        # are 32 * 0.3 = 9.6 wide from the geometry's own minimum, so the town's seams fall at 9.35,
        # 18.95 and 28.55, and a ray along x = z from an integer start runs straight at the point
        # where four of them meet. The town's "45 degrees" row stops at 28.55 and not at the wall's
        # eroded face at 28.85, which the head-on row above it gives. That row is KEPT: the barrier
        # is a polygon edge on the seam and it survives perturbing the ray's direction.
        #
        # What is excluded is the case that does NOT survive. Thirty-five rays along x = z through
        # that corner were run, over five start points and seven endpoints; from the start (2, 2),
        # varying only how far the ray is asked to go:
        #
        #   to (12, 12)   len 14.14   reached
        #   to (15, 15)   len 18.38   CLAMPED at 9.35
        #   to (20, 20)   len 25.46   reached
        #   to (24, 24)   len 31.11   reached
        #   to (27, 27)   len 35.36   CLAMPED at 9.35
        #   to (32, 32)   len 42.43   CLAMPED at 28.55
        #   to (40, 40)   len 53.74   CLAMPED at 9.35
        #
        # ONE START, ONE LINE, AND NEITHER THE OUTCOME NOR THE STOPPING POINT IS MONOTONIC IN
        # LENGTH: it reaches at 25.46 having stopped at 18.38, and the 53.74 ray stops EARLIER than
        # the 42.43 one. So no threshold on distance reproduces the column, and (2, 2) both reaches
        # and clamps, so the start does not explain it either. That is a float knife edge, and two
        # Recast implementations can legitimately fall either side of one.
        #
        # So none of the sweep's rays is vendored: no row in this file runs through that corner. The
        # three rows that do lie on x = z all start past it -- at 25 and 45 on the town, and at 15
        # on a layout whose own grid starts elsewhere -- and the straight seam crossings are all
        # off-corner for the same reason.
        #
        # Clamps at 28.55 in that sweep are NOT this: that is the second corner, where the town's
        # two walls also meet, and it is the row this file keeps because it survives perturbation.
        #
        # THE SWEEP IS NOT COMMITTED and was run out of band. To redo it, add rays along x = z to
        # the town layout's list in NavmeshVectors.cs and export to a scratch --out; nothing else
        # has to change. It is not kept as rows because rows here are a conformance fixture, and
        # these are exactly the answers a conformant client may disagree with.
        #
        # Floats are round-trip ("R") formatted; compare positions and heights by value with an
        # epsilon, and the outcome exactly.

        """;
}
