using System.Text.Json;
using Avalon.ChunkGen;
using Avalon.Common.Mathematics;
using Avalon.World.ChunkLayouts;
using Avalon.World.Maps.Navigation;
using Avalon.World.Testing.Scenarios;

namespace Avalon.Server.World.UnitTests.Procedural;

/// <summary>
/// The forest's generated pieces (forest content pass): what is committed is what the tool writes, decor included,
/// every slot and exit is clear of blockers, every doodad slot stands off the paths, and the baked navmesh lets a
/// creature stand on every slot and walk from every exit to every slot (doodad slots included), across a set piece's
/// open inner edges too, with the largest seeded solid doodad's box baked at every doodad_large slot. The committed
/// decor keeps the forest decor rules, and every path meets its neighbour's at the exit whatever the rotation.
/// </summary>
public class ForestPiecesShould
{
    private const float SlotClearance = 2.5f;
    /// <summary>How far every slot stands from its cell's edges: a solid doodad's box must fit inside it (DoodadCollisionShould).</summary>
    internal const float EdgeClearance = 2f;

    // Doodad slots stand off the paths and clear of blockers and spawns (doodads spec): a "doodad_large" slot may hold a
    // solid doodad, so it keeps further away. A path runs from an exit's throat centre to the piece centre (for a set
    // piece, its frame's centre, and between every two of its outer exits).
    private const float DoodadSlotClearance = 2.5f;
    private const float LargeDoodadSlotClearance = 4f;
    private const float DoodadPathClearance = 4f;
    private const float LargeDoodadPathClearance = 5f;
    private const float DoodadSpawnClearance = 3f;
    private const float LargeDoodadSpawnClearance = 4f;

    private static string Maps => Path.Combine(RepositoryRoot(), "src", "Server", "Avalon.Server.World", "Maps");

    public static IEnumerable<object[]> Singles() => ForestPieces.Singles().Select(p => new object[] { p.Name });

    public static IEnumerable<object[]> Groups() => ForestPieces.Groups().Select(g => new object[] { g.Name });

    /// <summary>The files the tool's run writes (ChunkFiles.For over the singles and the set pieces, as ChunkGenCli calls it).</summary>
    [Fact]
    public void Match_the_committed_files()
    {
        foreach ((string name, string obj, string json, string? decor) in ChunkFiles.For(ForestPieces.Singles(), ForestPieces.Groups()))
        {
            Assert.Equal(obj, Lf(File.ReadAllText(Path.Combine(Maps, "Chunks", name + ".obj"))));
            Assert.Equal(json, Lf(File.ReadAllText(Path.Combine(Maps, "Chunks", name + ".json"))));
            Assert.Equal(decor, Lf(File.ReadAllText(Path.Combine(Maps, "Decor", name + ".json"))));
        }
    }

    /// <summary>
    /// The decor files as committed (a set piece's four put back together in its frame): solid models stand on
    /// blockers and every blocker has one, the paths reach every exit's midpoint and the hub, and cover keeps off them.
    /// </summary>
    [Fact]
    public void Keep_the_forest_decor_rules_in_every_committed_decor_file()
    {
        IReadOnlyDictionary<string, ModelBounds> kit = KitBounds.Load();
        foreach (ChunkPiece piece in ForestPieces.Singles())
            ForestDecorRules.Check(piece, CommittedDecor(piece.Name), kit);
        foreach (ChunkGroupPiece group in ForestPieces.Groups())
        {
            List<DecorItem> frame =
            [
                .. group.Members().SelectMany(m => CommittedDecor(m.Piece.Name)
                    .Select(d => d with { X = d.X + m.CellX * ChunkPiece.CellSize, Z = d.Z + m.CellZ * ChunkPiece.CellSize })),
            ];
            ForestDecorRules.Check(group, frame, kit);
        }
    }

    /// <summary>
    /// Review focus 1, paths that do not meet: in every chunk the tool writes and in each of the four rotations, a dirt
    /// segment of the committed decor ends on each exit's side, its end's two corners on the rotated side, centred on its
    /// midpoint and the fixed width apart, so it meets the neighbour's whichever way either chunk is turned.
    /// </summary>
    [Fact]
    public void End_every_path_on_its_exit_midpoint_at_the_fixed_width_in_every_rotation()
    {
        const float Cell = ChunkPiece.CellSize;
        foreach (ChunkPiece piece in Pieces())
        {
            List<DecorItem> segments = [.. CommittedDecor(piece.Name).Where(d => d.Model == ForestPaths.Segment)];
            foreach (Side side in piece.Exits)
            {
                (float midX, float midZ) = side switch
                {
                    Side.N => (Cell / 2f, Cell),
                    Side.S => (Cell / 2f, 0f),
                    Side.E => (Cell, Cell / 2f),
                    _ => (0f, Cell / 2f),
                };
                for (byte rotation = 0; rotation < 4; rotation++)
                {
                    Vector3 mid = ChunkRotation.LocalToWorld(midX, 0f, midZ, rotation, Cell, Vector3.zero);
                    bool onX = MathF.Abs(mid.x) < 0.01f || MathF.Abs(mid.x - Cell) < 0.01f;
                    bool met = segments.SelectMany(EndCorners).Any(end =>
                    {
                        Vector3 a = ChunkRotation.LocalToWorld(end.A.X, 0f, end.A.Z, rotation, Cell, Vector3.zero);
                        Vector3 b = ChunkRotation.LocalToWorld(end.B.X, 0f, end.B.Z, rotation, Cell, Vector3.zero);
                        bool onSide = onX ? MathF.Abs(a.x - mid.x) < 0.01f && MathF.Abs(b.x - mid.x) < 0.01f
                                          : MathF.Abs(a.z - mid.z) < 0.01f && MathF.Abs(b.z - mid.z) < 0.01f;
                        float width = MathF.Sqrt((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z));
                        float off = MathF.Sqrt(((a.x + b.x) / 2f - mid.x) * ((a.x + b.x) / 2f - mid.x) + ((a.z + b.z) / 2f - mid.z) * ((a.z + b.z) / 2f - mid.z));
                        return onSide && off < 0.01f && MathF.Abs(width - ForestPaths.Width) < 0.01f;
                    });
                    Assert.True(met, $"{piece.Name}: no path ends on its {side} exit's midpoint at {ForestPaths.Width} m in rotation {rotation}");
                }
            }
        }
    }

    /// <summary>The corners of each end of a dirt segment: the end of its centre line, half the strip's width (scaled) to either side.</summary>
    private static IEnumerable<((float X, float Z) A, (float X, float Z) B)> EndCorners(DecorItem segment)
    {
        double yaw = segment.Yaw * Math.PI / 180.0;
        float fx = (float)Math.Sin(yaw), fz = (float)Math.Cos(yaw);
        float half = ForestPaths.SegmentLength / 2f * segment.Scale, across = ForestPaths.Width / 2f * segment.Scale;
        foreach (float sign in new[] { -1f, 1f })
        {
            float ex = segment.X + sign * fx * half, ez = segment.Z + sign * fz * half;
            yield return ((ex + fz * across, ez - fx * across), (ex - fz * across, ez + fx * across));
        }
    }

    /// <summary>
    /// Each forest decor rule refuses the decor that breaks it, so the rules over the committed files prove something:
    /// a fresh dressing of the glade with one thing broken.
    /// </summary>
    [Theory]
    [InlineData("a tree off its blocker", "stands on no blocker")]
    [InlineData("a trunk with no tree", "an invisible wall")]
    [InlineData("grass on the strip", "stands on the path strip")]
    [InlineData("a path end off its exit", "no path strip ends at the N exit's midpoint")]
    [InlineData("a wider strip", "would not be 2.8 m wide")]
    [InlineData("a fern in a throat", "hide the way out")]
    [InlineData("a sharp bend", "more than 6: the strip would open a gap")]
    [InlineData("a gap before a segment", "does not start 0.2 m back over the end of the segment before it")]
    [InlineData("two strips at one height", "overlap at one height outside the hub")]
    [InlineData("tall cover on open floor", "walk-through cover there stays under 1.2 m")]
    [InlineData("a tree wall with too few trees", "a tree wall needs a tree every 3 m of its length")]
    public void Refuse_decor_that_breaks_a_forest_decor_rule(string broken, string refusal)
    {
        ChunkPiece glade = ForestPieces.Singles().Single(p => p.Name == "forest_glade_01");
        List<DecorItem> decor = [.. ForestDresser.Dress(glade)];
        IReadOnlyDictionary<string, ModelBounds> kit = KitBounds.Load();
        ForestDecorRules.Check(glade, decor, kit);   // as dressed, it passes

        CylinderBlocker trunk = glade.Blockers.OfType<CylinderBlocker>().First(b => b.Dressing == Dressing.Trunk);
        int tree = decor.FindIndex(d => d.Model != ForestPaths.Segment && MathF.Abs(d.X - trunk.X) < 0.01f && MathF.Abs(d.Z - trunk.Z) < 0.01f);
        int north = decor.FindIndex(d => d.Model == ForestPaths.Segment && d.Yaw == 180 && MathF.Abs(d.X - 15f) < 0.01f && MathF.Abs(d.Z - 29f) < 0.01f);
        switch (broken)
        {
            case "a tree off its blocker":
                decor[tree] = decor[tree] with { X = 15f, Z = 15f };
                break;
            case "a trunk with no tree":
                decor.RemoveAt(tree);
                break;
            case "grass on the strip":
                decor.Add(new DecorItem("", "Grass_Common_Short", 15f, 0f, 22f, 0));
                break;
            case "a path end off its exit":
                decor[north] = decor[north] with { X = 15.5f };
                break;
            case "a wider strip":
                decor[north] = decor[north] with { Scale = 1.2f };
                break;
            case "a fern in a throat":
                decor.Add(new DecorItem("", "Fern_1", 26f, 0f, 19f, 0));
                break;
            case "a sharp bend":
            {
                // The north path's second segment turned 20 degrees, still starting the overlap back over the first's
                // end at (15, 28) along its new direction, so it links and only its bend is wrong.
                int second = decor.FindIndex(d => d.Model == ForestPaths.Segment && MathF.Abs(d.X - 15f) < 0.3f && MathF.Abs(d.Z - 27.2f) < 0.1f);
                int yaw = (decor[second].Yaw + 20) % 360;
                (float fx, float fz) = Forward(yaw);
                (float startX, float startZ) = (15f - fx * ForestPaths.Overlap, 28f - fz * ForestPaths.Overlap);
                decor[second] = decor[second] with { X = startX + fx, Z = startZ + fz, Yaw = yaw };
                break;
            }
            case "a gap before a segment":
            {
                // The north path's second segment moved twice the overlap along its way: it starts that far ahead of the
                // first's end instead of back over it, which a distance alone would not tell apart.
                int second = decor.FindIndex(d => d.Model == ForestPaths.Segment && MathF.Abs(d.X - 15f) < 0.3f && MathF.Abs(d.Z - 27.2f) < 0.1f);
                (float fx, float fz) = Forward(decor[second].Yaw);
                decor[second] = decor[second] with { X = decor[second].X + fx * 2f * ForestPaths.Overlap, Z = decor[second].Z + fz * 2f * ForestPaths.Overlap };
                break;
            }
            case "two strips at one height":
                decor.Add(decor.Where(d => d.Model == ForestPaths.Segment).MinBy(d => MathF.Abs(d.X - 15f) + MathF.Abs(d.Z - 23f))!);
                break;
            case "tall cover on open floor":
                decor.Add(new DecorItem("", "Plant_1_Big", 20f, 0f, 20f, 0));
                break;
            case "a tree wall with too few trees":
            {
                // The longest wall keeps one of its trees, so it still has something solid on it but too few for its length.
                BoxBlocker wall = glade.Blockers.OfType<BoxBlocker>().Where(b => b.Dressing == Dressing.TreeWall)
                    .MaxBy(b => Math.Max(b.MaxX - b.MinX, b.MaxZ - b.MinZ))!;
                List<DecorItem> inside = [.. decor.Where(d => IsTree(d.Model) && wall.DistanceTo(d.X, d.Z) == 0f)];
                Assert.True(inside.Count > 1, "the glade's longest tree wall holds fewer than two trees as dressed");
                inside.Skip(1).ToList().ForEach(d => decor.Remove(d));
                break;
            }
        }

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => ForestDecorRules.Check(glade, decor, kit));
        Assert.Contains("forest_glade_01", error.Message, StringComparison.Ordinal);
        Assert.Contains(refusal, error.Message, StringComparison.Ordinal);
    }

    private static bool IsTree(string model) =>
        Enum.GetNames<TreeSpecies>().Any(s => model.StartsWith(s + "_", StringComparison.Ordinal));

    /// <summary>
    /// The forest's density comes from tree walls (forest dressing, #1109): every closed side of a single piece, and every
    /// closed outer edge of a set piece in its 60 x 60 m frame, carries a TreeWall box against that edge and running along it.
    /// </summary>
    [Fact]
    public void Carry_a_tree_wall_along_every_closed_side()
    {
        foreach (ChunkPiece piece in ForestPieces.Singles())
        {
            foreach (Side side in Enum.GetValues<Side>().Except(piece.Exits))
                Assert.True(WallAlong(piece.Blockers, side, 0, 0), $"{piece.Name}: no tree wall along its closed {side} side");
        }

        foreach (ChunkGroupPiece group in ForestPieces.Groups())
        {
            foreach ((int cellX, int cellZ) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) })
            {
                Side[] outer = [cellZ == 0 ? Side.S : Side.N, cellX == 0 ? Side.W : Side.E];
                foreach (Side side in outer.Except(group.OuterExits.Where(e => e.CellX == cellX && e.CellZ == cellZ).Select(e => e.Side)))
                {
                    Assert.True(WallAlong(group.Blockers, side, cellX, cellZ),
                        $"{group.Name}: no tree wall along the closed {side} edge of its cell ({cellX}, {cellZ})");
                }
            }
        }
    }

    /// <summary>Whether a TreeWall box in cell (cellX, cellZ) lies against its <paramref name="side" /> edge (within 0.5 m) and runs along it.</summary>
    private static bool WallAlong(IEnumerable<Blocker> blockers, Side side, int cellX, int cellZ)
    {
        const float Cell = ChunkPiece.CellSize, Against = 0.5f;
        float ox = cellX * Cell, oz = cellZ * Cell;
        return blockers.OfType<BoxBlocker>().Where(b => b.Dressing == Dressing.TreeWall).Any(b =>
        {
            float alongX = b.MaxX - b.MinX, alongZ = b.MaxZ - b.MinZ;
            bool inCell = b.MinX >= ox && b.MaxX <= ox + Cell && b.MinZ >= oz && b.MaxZ <= oz + Cell;
            return inCell && side switch
            {
                Side.S => b.MinZ - oz <= Against && alongX >= alongZ,
                Side.N => oz + Cell - b.MaxZ <= Against && alongX >= alongZ,
                Side.W => b.MinX - ox <= Against && alongZ >= alongX,
                _ => ox + Cell - b.MaxX <= Against && alongZ >= alongX,
            };
        });
    }

    /// <summary>The direction a model's +Z points at a yaw, clockwise seen from above.</summary>
    private static (float X, float Z) Forward(int yaw) => ((float)Math.Sin(yaw * Math.PI / 180.0), (float)Math.Cos(yaw * Math.PI / 180.0));

    /// <summary>A committed decor file's items (Maps/Decor/&lt;name&gt;.json), as the client reads them.</summary>
    private static List<DecorItem> CommittedDecor(string name)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Maps, "Decor", name + ".json")));
        return
        [
            .. document.RootElement.GetProperty("items").EnumerateArray().Select(i => new DecorItem("", i.GetProperty("model").GetString()!,
                i.GetProperty("x").GetSingle(), i.GetProperty("y").GetSingle(), i.GetProperty("z").GetSingle(), i.GetProperty("yaw").GetInt32(),
                i.TryGetProperty("scale", out JsonElement scale) ? scale.GetSingle() : 1f)),
        ];
    }

    /// <summary>
    /// Every forest chunk committed is one the tool writes, but forest_boss_01 (hand-made, out of the pool): the pieces
    /// once written by hand are generated, so they are dressed and checked as the rest are.
    /// </summary>
    [Fact]
    public void Generate_thirty_two_pieces_and_three_set_pieces_and_every_forest_chunk_but_the_old_boss()
    {
        Assert.Equal(32, ForestPieces.Singles().Count);
        Assert.Equal(["forest_arena", "forest_clearing_big", "forest_grove_ruin"], ForestPieces.Groups().Select(g => g.Name).Order());
        var written = ChunkFiles.For(ForestPieces.Singles(), ForestPieces.Groups()).Select(f => f.Name).ToList();
        Assert.Equal(44, written.Count);
        Assert.Equal(["forest_boss_01"], Directory.EnumerateFiles(Path.Combine(Maps, "Chunks"), "forest_*.json")
            .Select(Path.GetFileNameWithoutExtension).OfType<string>().Except(written).Order(StringComparer.Ordinal));
    }

    /// <summary>Spawn slots and portal slots (the entry's way back to town) alike.</summary>
    [Fact]
    public void Keep_every_slot_clear_of_blockers_and_edges()
    {
        foreach (ChunkPiece piece in Pieces())
        {
            List<(float X, float Z, object Slot)> slots =
            [
                .. piece.Slots.Select(s => (s.X, s.Z, (object)s)), .. piece.PortalSlots.Select(s => (s.X, s.Z, (object)s)),
            ];
            foreach ((float x, float z, object slot) in slots)
            {
                Assert.InRange(x, EdgeClearance, ChunkPiece.CellSize - EdgeClearance);
                Assert.InRange(z, EdgeClearance, ChunkPiece.CellSize - EdgeClearance);
                foreach (Blocker blocker in piece.Blockers)
                {
                    Assert.True(blocker.DistanceTo(x, z) >= SlotClearance,
                        $"{piece.Name}: {slot} is {blocker.DistanceTo(x, z):0.00} m from {blocker}");
                }
            }
        }
    }

    [Fact]
    public void Keep_every_doodad_slot_off_paths_and_clear_of_blockers_spawns_and_edges()
    {
        Assert.All(ForestPieces.Singles(), p =>
        {
            Assert.Equal(2, p.DoodadSlots.Count(s => s.Tag == "doodad"));
            Assert.Equal(1, p.DoodadSlots.Count(s => s.Tag == "doodad_large"));
        });
        Assert.All(ForestPieces.Groups().SelectMany(g => g.Members()), m => Assert.Equal(1, m.Piece.DoodadSlots.Count(s => s.Tag == "doodad")));

        // Every piece the tool writes, in its own cell: away from the edges and out of its exits' throats.
        foreach (ChunkPiece piece in Pieces())
        {
            foreach (DoodadSlot slot in piece.DoodadSlots)
            {
                Assert.True(slot.Tag is "doodad" or "doodad_large", $"{piece.Name}: {slot} has an unknown tag");
                Assert.InRange(slot.X, EdgeClearance, ChunkPiece.CellSize - EdgeClearance);
                Assert.InRange(slot.Z, EdgeClearance, ChunkPiece.CellSize - EdgeClearance);
                foreach (Side side in piece.Exits)
                {
                    (float minX, float maxX, float minZ, float maxZ) = Throat(side);
                    Assert.False(slot.X >= minX && slot.X <= maxX && slot.Z >= minZ && slot.Z <= maxZ, $"{piece.Name}: {slot} stands in its {side} exit");
                }
            }
        }

        // A single piece's paths run from each exit's throat centre to the piece centre.
        const float Centre = ChunkPiece.CellSize / 2f;
        foreach (ChunkPiece piece in ForestPieces.Singles())
        {
            var paths = piece.Exits.Select(e => (From: ThroatCentre(e, 0, 0), To: (X: Centre, Z: Centre))).ToList();
            AssertDoodadSlotsClear(piece.Name, piece.DoodadSlots, piece.Blockers, piece.Slots, paths);
        }

        // A set piece's paths cross its inner edges: from each outer exit's throat centre to the frame centre, and
        // straight between every two outer exits. Checked in its 60 x 60 m frame, against all its blockers and slots.
        foreach (ChunkGroupPiece group in ForestPieces.Groups())
        {
            var exits = group.OuterExits.Select(e => ThroatCentre(e.Side, e.CellX, e.CellZ)).ToList();
            List<((float X, float Z) From, (float X, float Z) To)> paths = [.. exits.Select(e => (e, (ChunkPiece.CellSize, ChunkPiece.CellSize)))];
            for (int i = 0; i < exits.Count; i++)
            {
                for (int j = i + 1; j < exits.Count; j++)
                    paths.Add((exits[i], exits[j]));
            }
            AssertDoodadSlotsClear(group.Name, group.DoodadSlots, group.Blockers, group.Slots, paths);
        }
    }

    private static void AssertDoodadSlotsClear(string name, IEnumerable<DoodadSlot> doodadSlots, IEnumerable<Blocker> blockers,
        IEnumerable<Slot> spawns, IEnumerable<((float X, float Z) From, (float X, float Z) To)> paths)
    {
        foreach (DoodadSlot slot in doodadSlots)
        {
            bool large = slot.Tag == "doodad_large";
            foreach (Blocker blocker in blockers)
            {
                Assert.True(blocker.DistanceTo(slot.X, slot.Z) >= (large ? LargeDoodadSlotClearance : DoodadSlotClearance),
                    $"{name}: {slot} is {blocker.DistanceTo(slot.X, slot.Z):0.00} m from {blocker}");
            }
            foreach (Slot spawn in spawns)
            {
                float d = MathF.Sqrt((slot.X - spawn.X) * (slot.X - spawn.X) + (slot.Z - spawn.Z) * (slot.Z - spawn.Z));
                Assert.True(d >= (large ? LargeDoodadSpawnClearance : DoodadSpawnClearance), $"{name}: {slot} is {d:0.00} m from {spawn}");
            }
            foreach (((float X, float Z) from, (float X, float Z) to) in paths)
            {
                float d = DistanceToSegment(slot.X, slot.Z, from.X, from.Z, to.X, to.Z);
                Assert.True(d >= (large ? LargeDoodadPathClearance : DoodadPathClearance), $"{name}: {slot} is {d:0.00} m from the path {from} to {to}");
            }
        }
    }

    /// <summary>An exit's throat (10 m wide, 6 m deep, centred on its side) holds no blocker.</summary>
    [Fact]
    public void Keep_every_exit_throat_clear()
    {
        foreach (ChunkPiece piece in Pieces())
        {
            foreach (Side side in piece.Exits)
            {
                (float minX, float maxX, float minZ, float maxZ) = Throat(side);
                foreach (Blocker blocker in piece.Blockers)
                {
                    (float MinX, float MaxX, float MinZ, float MaxZ) b = blocker.Bounds;
                    bool overlaps = b.MinX < maxX && b.MaxX > minX && b.MinZ < maxZ && b.MaxZ > minZ;
                    Assert.False(overlaps, $"{piece.Name}: {blocker} stands in its {side} exit");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Singles))]
    public void Stand_every_slot_on_the_navmesh_floor_of_a_piece(string name)
    {
        ChunkPiece piece = ForestPieces.Singles().Single(p => p.Name == name);
        MapNavigator navigator = GeneratedChunkBake.Bake([(ObjWriter.Write(piece), 0, 0)]);

        // Each slot sampled from the height it is written at: a spawn slot 1 m up, a doodad or portal slot on the floor.
        foreach ((float x, float y, float z) in piece.Slots.Select(s => (s.X, 1f, s.Z))
                     .Concat(piece.DoodadSlots.Select(s => (s.X, ChunkPiece.DoodadSlotY, s.Z)))
                     .Concat(piece.PortalSlots.Select(s => (s.X, ChunkPiece.PortalSlotY, s.Z))))
        {
            Assert.Equal(NavmeshGroundKind.Under, navigator.FindGround(new Vector3(x, y, z), out Vector3 ground));
            Assert.InRange(ground.y, -0.3f, 0.3f);   // the floor, never a blocker's top
        }
    }

    /// <summary>
    /// With the largest seeded solid doodad's box baked at every doodad_large slot (as a plan may put one there), every
    /// exit, spawn slot, doodad slot and portal slot is reached from the first exit, and a large slot's doodad from
    /// beside its box.
    /// </summary>
    [Theory]
    [MemberData(nameof(Singles))]
    public void Walk_between_every_exit_and_every_slot_of_a_piece(string name)
    {
        ChunkPiece piece = ForestPieces.Singles().Single(p => p.Name == name);
        MapNavigator navigator = GeneratedChunkBake.Bake([(ObjWriter.Write(piece), 0, 0)], LargestSolidBoxes(piece.DoodadSlots));
        List<Vector3> targets =
        [
            .. piece.Exits.Select(e => ExitPoint(e, 0, 0)), .. piece.Slots.Select(s => new Vector3(s.X, 1f, s.Z)),
            .. piece.DoodadSlots.Where(s => s.Tag != "doodad_large").Select(s => new Vector3(s.X, ChunkPiece.DoodadSlotY, s.Z)),
            .. piece.PortalSlots.Select(s => new Vector3(s.X, ChunkPiece.PortalSlotY, s.Z)),
        ];

        AssertAllConnected(navigator, targets, name);
        AssertLargeSlotsReached(navigator, targets[0], piece.DoodadSlots, name);
    }

    /// <summary>
    /// The four members baked together, as the generator places them unrotated: their inner edges must be open. Every
    /// slot stands on the floor of the bare bake; the walks are made with the largest solid box at every doodad_large slot.
    /// </summary>
    [Theory]
    [MemberData(nameof(Groups))]
    public void Walk_across_a_set_pieces_open_inner_edges(string name)
    {
        ChunkGroupPiece group = ForestPieces.Groups().Single(g => g.Name == name);
        var members = group.Members().Select(m => (ObjWriter.Write(m.Piece), m.CellX, m.CellZ)).ToList();
        MapNavigator bare = GeneratedChunkBake.Bake(members);
        foreach ((float x, float y, float z) in group.Slots.Select(s => (s.X, 1f, s.Z))
                     .Concat(group.DoodadSlots.Select(s => (s.X, ChunkPiece.DoodadSlotY, s.Z))))
        {
            Assert.Equal(NavmeshGroundKind.Under, bare.FindGround(new Vector3(x, y, z), out Vector3 ground));
            Assert.InRange(ground.y, -0.3f, 0.3f);
        }

        MapNavigator navigator = GeneratedChunkBake.Bake(members, LargestSolidBoxes(group.DoodadSlots));
        List<Vector3> targets =
        [
            .. group.OuterExits.Select(e => ExitPoint(e.Side, e.CellX, e.CellZ)), .. group.Slots.Select(s => new Vector3(s.X, 1f, s.Z)),
            .. group.DoodadSlots.Where(s => s.Tag != "doodad_large").Select(s => new Vector3(s.X, ChunkPiece.DoodadSlotY, s.Z)),
        ];
        AssertAllConnected(navigator, targets, name);
        AssertLargeSlotsReached(navigator, targets[0], group.DoodadSlots, name);
    }

    /// <summary>
    /// The box of the largest solid doodad seeded (its widest half extent on both axes, so a turn changes nothing, and
    /// its greatest height) at every doodad_large slot, standing on the slot as a placed solid doodad does.
    /// </summary>
    private static List<DoodadObstacle> LargestSolidBoxes(IEnumerable<DoodadSlot> slots)
    {
        (float half, float height) = LargestSolidBox();
        return
        [
            .. slots.Where(s => s.Tag == "doodad_large").Select(s => new DoodadObstacle(s.X - half, s.X + half, s.Z - half,
                s.Z + half, ChunkPiece.DoodadSlotY, ChunkPiece.DoodadSlotY + height)),
        ];
    }

    /// <summary>The widest half extent and the greatest height of the seeded solid doodads (the World seed, as loaded).</summary>
    private static (float Half, float Height) LargestSolidBox()
    {
        var solid = ScenarioReferenceData.Shared.Data.Doodads.Templates.Where(t => t.Solid).ToList();
        Assert.NotEmpty(solid);
        return (solid.Max(t => Math.Max(t.CollisionHalfX, t.CollisionHalfZ)), solid.Max(t => t.CollisionHeight));
    }

    /// <summary>
    /// A doodad at a doodad_large slot, its box baked, is reached when a walk from <paramref name="from" /> ends beside
    /// the box, a metre out on one of its four sides: within any doodad's use range.
    /// </summary>
    private static void AssertLargeSlotsReached(MapNavigator navigator, Vector3 from, IEnumerable<DoodadSlot> slots, string name)
    {
        float reach = LargestSolidBox().Half + 1f;
        foreach (DoodadSlot slot in slots.Where(s => s.Tag == "doodad_large"))
        {
            Vector3[] beside =
            [
                new(slot.X - reach, ChunkPiece.DoodadSlotY, slot.Z), new(slot.X + reach, ChunkPiece.DoodadSlotY, slot.Z),
                new(slot.X, ChunkPiece.DoodadSlotY, slot.Z - reach), new(slot.X, ChunkPiece.DoodadSlotY, slot.Z + reach),
            ];
            Assert.True(beside.Any(to => Reaches(navigator, from, to)), $"{name}: no walk from {from} reaches the box at {slot}");
        }
    }

    private static bool Reaches(MapNavigator navigator, Vector3 from, Vector3 to)
    {
        List<Vector3> path = navigator.FindPath(from, to);
        if (path.Count == 0) return false;
        Vector3 end = path[^1];
        return (end.x - to.x) * (end.x - to.x) + (end.z - to.z) * (end.z - to.z) <= 1f;
    }

    [Fact]
    public void Carry_a_boss_only_in_the_arena_and_leaders_in_every_set_piece()
    {
        Assert.All(ForestPieces.Singles(), p => Assert.DoesNotContain(p.Slots, s => s.Tag is "boss" or "leader"));
        Assert.Single(ForestPieces.Groups().Single(g => g.Name == "forest_arena").Slots, s => s.Tag == "boss");
        Assert.All(ForestPieces.Groups(), g => Assert.True(g.Slots.Count(s => s.Tag == "leader") >= 2, $"{g.Name} has fewer than two leaders"));
    }

    /// <summary>chunk-groups.json lists exactly the generated set pieces and their cells; chunk-pools.json lists every single piece (by name or as a weighted entry) and no member.</summary>
    [Fact]
    public void List_the_pieces_in_the_pool_and_group_files()
    {
        using var pools = JsonDocument.Parse(File.ReadAllText(Path.Combine(Maps, "chunk-pools.json")));
        var forest = pools.RootElement.GetProperty("forest_pool").EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.Object ? e.GetProperty("chunk").GetString()! : e.GetString()!).ToList();
        Assert.All(ForestPieces.Singles(), p => Assert.Contains(p.Name, forest));
        Assert.DoesNotContain(forest, n => ForestPieces.Groups().Any(g => n.StartsWith(g.Name + "_", StringComparison.Ordinal)));
        Assert.DoesNotContain("forest_boss_01", forest);   // owner decision 5

        using var groups = JsonDocument.Parse(File.ReadAllText(Path.Combine(Maps, "chunk-groups.json")));
        var listed = groups.RootElement.GetProperty("forest_pool").EnumerateArray().ToDictionary(
            g => g.GetProperty("name").GetString()!,
            g => g.GetProperty("members").EnumerateArray()
                .Select(m => (m.GetProperty("chunk").GetString()!, m.GetProperty("cellX").GetInt32(), m.GetProperty("cellZ").GetInt32()))
                .OrderBy(m => m.Item1, StringComparer.Ordinal).ToList());
        Assert.Equal(ForestPieces.Groups().Select(g => g.Name).Order(), listed.Keys.Order());
        foreach (ChunkGroupPiece group in ForestPieces.Groups())
            Assert.Equal(group.Members().Select(m => (m.Piece.Name, m.CellX, m.CellZ)).OrderBy(m => m.Name, StringComparer.Ordinal), listed[group.Name]);
    }

    /// <summary>An exit's throat: 10 m wide and 6 m deep, centred on its side.</summary>
    private static (float MinX, float MaxX, float MinZ, float MaxZ) Throat(Side side) => side switch
    {
        Side.N => (10f, 20f, 24f, 30f),
        Side.S => (10f, 20f, 0f, 6f),
        Side.E => (24f, 30f, 10f, 20f),
        _ => (0f, 6f, 10f, 20f),
    };

    /// <summary>The centre of an exit's throat, in the frame whose cell (cellX, cellZ) holds the exit.</summary>
    private static (float X, float Z) ThroatCentre(Side side, int cellX, int cellZ)
    {
        (float minX, float maxX, float minZ, float maxZ) = Throat(side);
        return ((minX + maxX) / 2f + cellX * ChunkPiece.CellSize, (minZ + maxZ) / 2f + cellZ * ChunkPiece.CellSize);
    }

    private static float DistanceToSegment(float px, float pz, float ax, float az, float bx, float bz)
    {
        float dx = bx - ax, dz = bz - az;
        float t = Math.Clamp(((px - ax) * dx + (pz - az) * dz) / (dx * dx + dz * dz), 0f, 1f);
        float cx = ax + t * dx - px, cz = az + t * dz - pz;
        return MathF.Sqrt(cx * cx + cz * cz);
    }

    /// <summary>A point just inside the middle of a side: the floor's edge is eroded by the agent radius (0.6 m).</summary>
    private static Vector3 ExitPoint(Side side, int cellX, int cellZ)
    {
        float ox = cellX * 30f, oz = cellZ * 30f;
        return side switch
        {
            Side.N => new Vector3(ox + 15f, 1f, oz + 29f),
            Side.S => new Vector3(ox + 15f, 1f, oz + 1f),
            Side.E => new Vector3(ox + 29f, 1f, oz + 15f),
            _ => new Vector3(ox + 1f, 1f, oz + 15f),
        };
    }

    private static void AssertAllConnected(MapNavigator navigator, List<Vector3> targets, string name)
    {
        Vector3 from = targets[0];
        foreach (Vector3 to in targets.Skip(1))
        {
            List<Vector3> path = navigator.FindPath(from, to);
            Assert.NotEmpty(path);
            Vector3 end = path[^1];
            float gap = MathF.Sqrt((end.x - to.x) * (end.x - to.x) + (end.z - to.z) * (end.z - to.z));
            Assert.True(gap <= 1f, $"{name}: the walk from {from} to {to} ends {gap:0.00} m short");
        }
    }

    /// <summary>Every piece the tool writes: the singles and each set piece's four members.</summary>
    private static List<ChunkPiece> Pieces() =>
        [.. ForestPieces.Singles(), .. ForestPieces.Groups().SelectMany(g => g.Members().Select(m => m.Piece))];

    private static string Lf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Avalon.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("No Avalon.sln above the test output.");
    }
}
