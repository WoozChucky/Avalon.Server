namespace Avalon.ChunkGen;

/// <summary>
/// What a player sees in the forest and what they collide with agree, and the paths meet (forest dressing, #1109).
/// A forest piece's decor (a set piece's in its 60 x 60 m frame) is refused unless:
/// <list type="bullet">
/// <item>every item is a forest model the kit's bounds table has, at a yaw of 0-359, and stands inside the frame;</item>
/// <item>every solid part (a tree's trunk, a kit rock's turned box, each footprint part of a custom model, which is
/// placed at scale 1 and turned by quarter turns, or by multiples of 30 degrees when its footprint is round) lies within
/// <see cref="Tolerance" /> of one blocker: nothing drawn solid stands where a player walks;</item>
/// <item>every blocker has a solid part within it: no invisible wall;</item>
/// <item>a path strip of <see cref="ForestPaths.Width" /> (every segment at scale 1) ends at the midpoint of each exit's
/// side, running straight in from it, and its segments, each starting <see cref="ForestPaths.Overlap" /> back over the
/// one before (within <see cref="PathTolerance" />), reach the one hub, bending at most <see cref="ForestPaths.MaxBend" />
/// degrees a segment and each at another height than the one before; no segment lies outside the frame or off every
/// exit's path, and no two segments overlap at one height outside the hub;</item>
/// <item>no blocker reaches onto a strip, and nothing walk-through but a stepping stone stands on one: ground cover keeps
/// <see cref="StripClearance" /> from every segment's centre line and <see cref="HubClearance" /> from the hub, and a
/// stepping stone stands within <see cref="StoneReach" /> of a centre line or on the hub;</item>
/// <item>inside an exit's throat, ground cover is grass or low (<see cref="LowCover" />), so nothing hides the way
/// out: grass beside the strip at the edge is fine;</item>
/// <item>walk-through cover drawn taller than <see cref="WalkThroughCap" /> stands within a blocker (a drum's or a
/// rock box's fill): on open floor what a player walks through reads as small.</item>
/// </list>
/// Each refusal (InvalidOperationException) names the piece and the model, part or exit.
/// </summary>
public static class ForestDecorRules
{
    /// <summary>How far a solid part may reach past the blocker it stands on, as the town's decor may (DecorRules.Tolerance).</summary>
    public const float Tolerance = 0.15f;

    /// <summary>How closely a path's end meets an exit's midpoint, and the next segment's start lies <see cref="ForestPaths.Overlap" /> back from it.</summary>
    public const float PathTolerance = 0.01f;

    /// <summary>How far ground cover stands from a strip's centre line: half the strip and 0.6 m.</summary>
    public const float StripClearance = ForestPaths.Width / 2f + 0.6f;

    /// <summary>How far ground cover stands from the hub's centre.</summary>
    public const float HubClearance = ForestPaths.HubRadius + 0.5f;

    /// <summary>How far a stepping stone may stand from a strip's centre line: well inside the strip.</summary>
    public const float StoneReach = ForestPaths.Width / 2f - 0.5f;

    /// <summary>The tallest ground cover, drawn, that may stand in an exit's throat besides grass.</summary>
    public const float LowCover = 0.6f;

    /// <summary>The tallest walk-through cover, drawn (its model's top times its scale), that may stand off a blocker.</summary>
    public const float WalkThroughCap = 1.2f;

    /// <summary>What a forest model is to the rules.</summary>
    internal enum Kind { Tree, KitRock, Prop, Path, Stone, Cover }

    private static readonly string[] s_coverPrefixes = ["Grass_", "Fern_", "Clover_", "Plant_", "Bush_", "Flower_", "Mushroom_", "Pebble_", "Petal_"];

    public static void Check(ChunkPiece piece, IReadOnlyList<DecorItem> decor, IReadOnlyDictionary<string, ModelBounds> kit) =>
        Check(ForestFrame.Of(piece), decor, kit);

    /// <summary>A set piece's decor, in its 60 x 60 m frame (before it is split into members).</summary>
    public static void Check(ChunkGroupPiece group, IReadOnlyList<DecorItem> decor, IReadOnlyDictionary<string, ModelBounds> kit) =>
        Check(ForestFrame.Of(group), decor, kit);

    internal static void Check(ForestFrame frame, IReadOnlyList<DecorItem> decor, IReadOnlyDictionary<string, ModelBounds> kit)
    {
        IReadOnlyDictionary<string, IReadOnlyList<FootprintPart>> footprints = ForestFootprints.Load();
        bool[] covered = new bool[frame.Blockers.Count];
        List<DecorItem> segments = [], hubs = [], stones = [], cover = [];

        foreach (DecorItem item in decor)
        {
            string what = What(frame, item);
            item.Check(frame.Name, kit, DecorYaw.AnyDegree);
            if (!(item.X >= 0f && item.X <= frame.Size && item.Z >= 0f && item.Z <= frame.Size))
                throw new InvalidOperationException($"{what} stands outside the {ForestText.N(frame.Size)} x {ForestText.N(frame.Size)} m frame");

            Kind kind = KindOf(item.Model, footprints);
            switch (kind)
            {
                case Kind.Path:
                    (item.Model == ForestPaths.Hub ? hubs : segments).Add(item);
                    continue;
                case Kind.Stone:
                    stones.Add(item);
                    continue;
                case Kind.Cover:
                    cover.Add(item);
                    continue;
            }

            foreach ((string part, Shape shape) in SolidShapes(item, kind, kit, footprints, what))
            {
                bool within = false;
                for (int b = 0; b < frame.Blockers.Count; b++)
                {
                    if (!Within(shape, frame.Blockers[b], Tolerance)) continue;
                    covered[b] = true;
                    within = true;
                }
                if (!within)
                    throw new InvalidOperationException($"{what}: its {part} stands on no blocker: a player would walk through it");
            }
        }

        for (int b = 0; b < frame.Blockers.Count; b++)
        {
            if (!covered[b])
                throw new InvalidOperationException($"{frame.Name}: blocker {ForestText.Of(frame.Blockers[b])} has nothing solid drawn on it: an invisible wall");
        }

        CheckPaths(frame, segments, hubs);

        List<((float X, float Z) A, (float X, float Z) B)> strip = [.. segments.Select(ForestPaths.Ends)];
        (float hubX, float hubZ) = hubs.Count == 1 ? (hubs[0].X, hubs[0].Z) : (float.NaN, float.NaN);
        foreach (Blocker blocker in frame.Blockers)
        {
            foreach (((float X, float Z) a, (float X, float Z) b) in strip)
            {
                for (int i = 0; i <= 8; i++)
                {
                    float t = i / 8f;
                    if (blocker.DistanceTo(a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t) < ForestPaths.Width / 2f)
                        throw new InvalidOperationException($"{frame.Name}: blocker {ForestText.Of(blocker)} reaches onto the path strip");
                }
            }
        }

        foreach (DecorItem item in cover)
        {
            string what = What(frame, item);
            if (strip.Any(s => ForestPaths.DistanceToSegment(item.X, item.Z, s.A, s.B) < StripClearance)
                || (hubs.Count == 1 && ForestPaths.Distance(item.X, item.Z, hubX, hubZ) < HubClearance))
            {
                throw new InvalidOperationException($"{what} stands on the path strip: only stepping stones may");
            }
            ForestExit? throat = frame.Exits.FirstOrDefault(e => e.InThroat(item.X, item.Z));
            if (throat is not null && !MayStandInThroat(item.Model, item.Scale, kit))
                throw new InvalidOperationException($"{what} stands in the {throat.Side} exit's throat, where only grass and low cover may: it would hide the way out");
            float drawn = kit[item.Model].MaxY * item.Scale;
            if (drawn > WalkThroughCap && !frame.Blockers.Any(b => b.DistanceTo(item.X, item.Z) == 0f))
            {
                throw new InvalidOperationException(
                    $"{what} is drawn {ForestText.N(drawn)} m tall on open floor: walk-through cover there stays under {ForestText.N(WalkThroughCap)} m");
            }
        }

        foreach (DecorItem stone in stones)
        {
            bool onStrip = strip.Any(s => ForestPaths.DistanceToSegment(stone.X, stone.Z, s.A, s.B) <= StoneReach)
                || (hubs.Count == 1 && ForestPaths.Distance(stone.X, stone.Z, hubX, hubZ) <= ForestPaths.HubRadius);
            if (!onStrip) throw new InvalidOperationException($"{What(frame, stone)} is a stepping stone off the path strip");
        }
    }

    /// <summary>
    /// The paths: one hub; every segment at scale 1 (so <see cref="ForestPaths.Width" /> wide) and inside the frame; from
    /// each exit's midpoint a segment running straight in, and a chain from it, each segment starting
    /// <see cref="ForestPaths.Overlap" /> back from the last one's end, turned at most <see cref="ForestPaths.MaxBend" />
    /// and at another height, to within <see cref="ForestPaths.HubReach" /> of the hub; no two segments overlapping at
    /// one height outside the hub; no segment on no exit's path.
    /// </summary>
    private static void CheckPaths(ForestFrame frame, List<DecorItem> segments, List<DecorItem> hubs)
    {
        if (frame.Exits.Count == 0 && segments.Count == 0 && hubs.Count == 0) return;
        if (hubs.Count != 1) throw new InvalidOperationException($"{frame.Name} has {hubs.Count} path hubs, not one where its paths meet");
        DecorItem hub = hubs[0];
        if (hub.Scale != 1f) throw new InvalidOperationException($"{frame.Name}: the path hub is drawn at scale {ForestText.N(hub.Scale)}, not 1");

        foreach (DecorItem segment in segments)
        {
            if (segment.Scale != 1f)
            {
                throw new InvalidOperationException(
                    $"{What(frame, segment)} is drawn at scale {ForestText.N(segment.Scale)}: the strip would not be {ForestText.N(ForestPaths.Width)} m wide");
            }
            foreach ((float x, float z) in Corners(segment))
            {
                if (x < -PathTolerance || z < -PathTolerance || x > frame.Size + PathTolerance || z > frame.Size + PathTolerance)
                    throw new InvalidOperationException($"{What(frame, segment)} reaches outside the frame");
            }
        }

        var reached = new HashSet<DecorItem>(ReferenceEqualityComparer.Instance);
        foreach (ForestExit exit in frame.Exits)
        {
            DecorItem? first = segments.FirstOrDefault(s =>
                (Yaw.Normal(s.Yaw) == exit.InwardYaw || Yaw.Normal(s.Yaw + 180) == exit.InwardYaw)
                && (Meets(ForestPaths.Ends(s).A, exit.X, exit.Z, 0f) || Meets(ForestPaths.Ends(s).B, exit.X, exit.Z, 0f)));
            if (first is null)
            {
                throw new InvalidOperationException(
                    $"{frame.Name}: no path strip ends at the {exit.Side} exit's midpoint ({ForestText.N(exit.X)}, {ForestText.N(exit.Z)}) " +
                    $"running straight in at {ForestText.N(ForestPaths.Width)} m: the paths would not meet");
            }

            DecorItem current = first;
            bool forward = Meets(ForestPaths.Ends(first).A, exit.X, exit.Z, 0f);
            (float X, float Z) end = forward ? ForestPaths.Ends(first).B : ForestPaths.Ends(first).A;
            reached.Add(current);
            while (ForestPaths.Distance(end.X, end.Z, hub.X, hub.Z) > ForestPaths.HubReach + PathTolerance)
            {
                (float X, float Z) at = end;
                DecorItem? next = segments.FirstOrDefault(s => !reached.Contains(s)
                    && (Meets(ForestPaths.Ends(s).A, at.X, at.Z, ForestPaths.Overlap) || Meets(ForestPaths.Ends(s).B, at.X, at.Z, ForestPaths.Overlap)));
                if (next is null)
                {
                    throw new InvalidOperationException(
                        $"{frame.Name}: the path from the {exit.Side} exit breaks off at ({ForestText.N(at.X)}, {ForestText.N(at.Z)}) before the hub");
                }

                bool nextForward = Meets(ForestPaths.Ends(next).A, at.X, at.Z, ForestPaths.Overlap);
                int bend = Math.Abs(Yaw.Normal(Travel(next, nextForward) - Travel(current, forward)));
                if (Math.Min(bend, 360 - bend) > ForestPaths.MaxBend)
                {
                    throw new InvalidOperationException(
                        $"{What(frame, next)} bends {Math.Min(bend, 360 - bend)} degrees from the segment before it, more than {ForestPaths.MaxBend}: the strip would open a gap");
                }
                if (MathF.Abs(next.Y - current.Y) < ForestPaths.Lift / 2f)
                    throw new InvalidOperationException($"{What(frame, next)} lies at the height of the segment before it: their overlap would draw two strips at one height");
                reached.Add(next);
                end = nextForward ? ForestPaths.Ends(next).B : ForestPaths.Ends(next).A;
                (current, forward) = (next, nextForward);
            }
        }

        CheckOverlaps(frame, segments, hub);

        DecorItem? stray = segments.FirstOrDefault(s => !reached.Contains(s));
        if (stray is not null) throw new InvalidOperationException($"{What(frame, stray)} is a path segment on no exit's path");
    }

    /// <summary>Whether a segment's end lies <paramref name="back" /> from (x, z), within <see cref="PathTolerance" />.</summary>
    private static bool Meets((float X, float Z) end, float x, float z, float back) =>
        MathF.Abs(ForestPaths.Distance(end.X, end.Z, x, z) - back) <= PathTolerance;

    /// <summary>The yaw a segment is walked along: its own from its A end, the reverse from its B end.</summary>
    private static int Travel(DecorItem segment, bool fromA) => fromA ? segment.Yaw : segment.Yaw + 180;

    /// <summary>
    /// No two segments at one height overlap outside the hub (whose top is above every strip): consecutive segments of a
    /// path differ in height by construction (checked above), so this catches strips of two paths, or of one path
    /// folding back, drawn over each other. A segment's strip is sampled every 0.25 m along and 0.35 m across.
    /// </summary>
    private static void CheckOverlaps(ForestFrame frame, List<DecorItem> segments, DecorItem hub)
    {
        const float Half = ForestPaths.SegmentLength / 2f, Across = ForestPaths.Width / 2f, Inset = 0.02f;
        for (int i = 0; i < segments.Count; i++)
        {
            DecorItem a = segments[i];
            (float ax, float az) = Yaw.Forward(a.Yaw);
            for (int j = i + 1; j < segments.Count; j++)
            {
                DecorItem b = segments[j];
                if (MathF.Abs(a.Y - b.Y) >= ForestPaths.Lift / 2f || ForestPaths.Distance(a.X, a.Z, b.X, b.Z) > 2f * (Half + Across)) continue;
                (float bx, float bz) = Yaw.Forward(b.Yaw);
                for (float u = -Half; u <= Half + 0.001f; u += 0.25f)
                {
                    for (float v = -Across; v <= Across + 0.001f; v += 0.35f)
                    {
                        float px = a.X + ax * u + az * v, pz = a.Z + az * u - ax * v;
                        if (ForestPaths.Distance(px, pz, hub.X, hub.Z) <= ForestPaths.HubRadius) continue;
                        float bu = (px - b.X) * bx + (pz - b.Z) * bz, bv = (px - b.X) * bz - (pz - b.Z) * bx;
                        if (MathF.Abs(bu) < Half - Inset && MathF.Abs(bv) < Across - Inset)
                        {
                            throw new InvalidOperationException(
                                $"{What(frame, a)} and the segment at ({ForestText.N(b.X)}, {ForestText.N(b.Z)}) overlap at one height outside the hub: they would z-fight");
                        }
                    }
                }
            }
        }
    }

    /// <summary>A segment's four corners: its centre line's ends, half the strip's width to either side.</summary>
    private static IEnumerable<(float X, float Z)> Corners(DecorItem segment)
    {
        ((float X, float Z) a, (float X, float Z) b) = ForestPaths.Ends(segment);
        (float fx, float fz) = Yaw.Forward(segment.Yaw);
        float half = ForestPaths.Width / 2f * segment.Scale;
        foreach ((float X, float Z) end in new[] { a, b })
        {
            yield return (end.X + fz * half, end.Z - fx * half);
            yield return (end.X - fz * half, end.Z + fx * half);
        }
    }

    /// <summary>Whether ground cover may stand in an exit's throat: grass, or cover no taller than <see cref="LowCover" /> drawn.</summary>
    public static bool MayStandInThroat(string model, float scale, IReadOnlyDictionary<string, ModelBounds> kit) =>
        model.StartsWith("Grass_", StringComparison.Ordinal) || (kit.TryGetValue(model, out ModelBounds? b) && b.MaxY * scale <= LowCover);

    /// <summary>What a model is: a tree, a kit rock, a custom solid model, a path piece, a stepping stone or ground cover; anything else is refused.</summary>
    internal static Kind KindOf(string model, IReadOnlyDictionary<string, IReadOnlyList<FootprintPart>> footprints)
    {
        if (footprints.TryGetValue(model, out IReadOnlyList<FootprintPart>? parts))
        {
            if (parts.Count > 0) return Kind.Prop;
            if (model is ForestPaths.Segment or ForestPaths.Hub) return Kind.Path;
            throw new InvalidOperationException($"{model} is a walk-through custom model the forest does not place");
        }
        if (SpeciesOf(model) is not null) return Kind.Tree;
        if (model.StartsWith("Rock_Medium_", StringComparison.Ordinal)) return Kind.KitRock;
        if (model.StartsWith("RockPath_", StringComparison.Ordinal)) return Kind.Stone;
        if (s_coverPrefixes.Any(p => model.StartsWith(p, StringComparison.Ordinal))) return Kind.Cover;
        throw new InvalidOperationException($"{model} is not a forest model");
    }

    private static TreeSpecies? SpeciesOf(string model)
    {
        int underscore = model.IndexOf('_', StringComparison.Ordinal);
        return underscore > 0 && Enum.TryParse(model[..underscore], out TreeSpecies species) && Enum.IsDefined(species) ? species : null;
    }

    /// <summary>The solid parts of a tree, a kit rock or a custom model, named for a refusal; a custom model off its scale or yaw rule is refused.</summary>
    private static IEnumerable<(string Part, Shape Shape)> SolidShapes(
        DecorItem item, Kind kind, IReadOnlyDictionary<string, ModelBounds> kit, IReadOnlyDictionary<string, IReadOnlyList<FootprintPart>> footprints, string what)
    {
        switch (kind)
        {
            case Kind.Tree:
                yield return ("trunk", new Circle(item.X, item.Z, ForestDresser.TrunkRadius(SpeciesOf(item.Model)!.Value) * item.Scale));
                break;
            case Kind.KitRock:
                yield return ("rock", KitRockShape(kit[item.Model], item.X, item.Z, item.Yaw, item.Scale));
                break;
            case Kind.Prop:
                if (item.Scale != 1f) throw new InvalidOperationException($"{what} is drawn at scale {ForestText.N(item.Scale)}: a custom forest model stands at 1, on its footprint");
                IReadOnlyList<FootprintPart> parts = footprints[item.Model];
                int step = parts.Any(p => p is BoxPart) ? 90 : 30;
                if (item.Yaw % step != 0) throw new InvalidOperationException($"{what} has yaw {item.Yaw}: its footprint turns only by multiples of {step} degrees");
                foreach (FootprintPart part in parts)
                    yield return (part.Part, PartShape(part, item.X, item.Z, item.Yaw));
                break;
        }
    }

    /// <summary>Whether every footprint part of a custom model at (x, z) and its yaw lies within the blocker.</summary>
    internal static bool PartsWithin(string model, IReadOnlyList<FootprintPart> parts, float x, float z, int yaw, Blocker blocker, float tolerance) =>
        parts.All(p => Within(PartShape(p, x, z, yaw), blocker, tolerance));

    /// <summary>Whether a point lies in one of a custom model's footprint parts at (x, z) and its yaw.</summary>
    internal static bool PartsCover(IReadOnlyList<FootprintPart> parts, float x, float z, int yaw, float px, float pz) =>
        parts.Any(p => PartShape(p, x, z, yaw) switch
        {
            Circle c => ForestPaths.Distance(px, pz, c.X, c.Z) <= c.R,
            Quad q => px >= q.Corners.Min(k => k.X) && px <= q.Corners.Max(k => k.X) && pz >= q.Corners.Min(k => k.Z) && pz <= q.Corners.Max(k => k.Z),
            _ => false,
        });

    /// <summary>Whether a kit rock's box, scaled and turned, at (x, z) lies within the blocker.</summary>
    internal static bool KitRockWithin(ModelBounds bounds, float x, float z, int yaw, float scale, Blocker blocker, float tolerance) =>
        Within(KitRockShape(bounds, x, z, yaw, scale), blocker, tolerance);

    private static Shape PartShape(FootprintPart part, float x, float z, int yaw)
    {
        switch (part)
        {
            case BoxPart box:
                (float minX, float maxX, float minZ, float maxZ) = TownBuilding.Footprint(box.MinX, box.MaxX, box.MinZ, box.MaxZ, x, z, yaw / 90);
                return new Quad([(minX, minZ), (maxX, minZ), (maxX, maxZ), (minX, maxZ)]);
            case CirclePart circle:
                (float cx, float cz) = Yaw.Turn(circle.X, circle.Z, yaw);
                return new Circle(x + cx, z + cz, circle.Radius);
            default:
                throw new InvalidOperationException($"part {part.Part} is a shape the rules do not know");
        }
    }

    private static Quad KitRockShape(ModelBounds b, float x, float z, int yaw, float scale) =>
        new([.. new[] { (b.MinX, b.MinZ), (b.MaxX, b.MinZ), (b.MaxX, b.MaxZ), (b.MinX, b.MaxZ) }.Select(c =>
        {
            (float tx, float tz) = Yaw.Turn(c.Item1 * scale, c.Item2 * scale, yaw);
            return (x + tx, z + tz);
        })]);

    /// <summary>
    /// Whether the shape lies within the blocker grown by the tolerance: a box's sides, or a twelve-gon's flat sides
    /// (its radius times <see cref="CylinderBlocker.Apothem" />), which a circle or a quad's corners must not pass.
    /// </summary>
    private static bool Within(Shape shape, Blocker blocker, float tolerance) => (shape, blocker) switch
    {
        (Circle c, BoxBlocker b) => c.X - c.R >= b.MinX - tolerance && c.X + c.R <= b.MaxX + tolerance
                                    && c.Z - c.R >= b.MinZ - tolerance && c.Z + c.R <= b.MaxZ + tolerance,
        (Circle c, CylinderBlocker d) => ForestPaths.Distance(c.X, c.Z, d.X, d.Z) + c.R <= d.Radius * CylinderBlocker.Apothem + tolerance,
        (Quad q, BoxBlocker b) => q.Corners.All(p => p.X >= b.MinX - tolerance && p.X <= b.MaxX + tolerance
                                                    && p.Z >= b.MinZ - tolerance && p.Z <= b.MaxZ + tolerance),
        (Quad q, CylinderBlocker d) => q.Corners.All(p => ForestPaths.Distance(p.X, p.Z, d.X, d.Z) <= d.Radius * CylinderBlocker.Apothem + tolerance),
        _ => false,
    };

    private static string What(ForestFrame frame, DecorItem item) => $"{frame.Name}: {item.Model} at ({ForestText.N(item.X)}, {ForestText.N(item.Z)})";

    private abstract record Shape;

    private sealed record Circle(float X, float Z, float R) : Shape;

    private sealed record Quad(IReadOnlyList<(float X, float Z)> Corners) : Shape;
}
