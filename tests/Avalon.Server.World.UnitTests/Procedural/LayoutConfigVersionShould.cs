using System.Globalization;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.Procedural;

public class LayoutConfigVersionShould
{
    private static ProceduralMapConfig Config(float branchChance = 0.25f) => new()
    {
        MapTemplateId = new MapTemplateId(12),
        ChunkPoolId = new ChunkPoolId(3),
        SpawnTableId = new SpawnTableId(1),
        MainPathMin = 5,
        MainPathMax = 9,
        BranchChance = branchChance,
        BranchMaxDepth = 2,
        HasBoss = true,
        BackPortalTargetMapId = 1,
        ForwardPortalTargetMapId = 14,
    };

    private static ChunkTemplate Template(ushort id, string geometry) => new()
    {
        Id = new ChunkTemplateId(id),
        Name = $"chunk-{id}",
        AssetKey = $"asset-{id}",
        GeometryFile = geometry,
        CellFootprintX = 1,
        CellFootprintZ = 1,
        CellSize = 30.0f,
        Exits = 0b1010,
    };

    private static List<ChunkPoolMember> Pool() =>
    [
        new(Template(1, "a.obj"), 1.0f),
        new(Template(2, "b.obj"), 2.5f),
    ];

    // Separate from Pool(): Should_be_carried_on_the_generated_layout needs a pool that
    // ProceduralLayoutGenerator.Generate can actually walk, not just one LayoutConfigVersion.Compute
    // can hash (Compute ignores SpawnSlots/PortalSlots entirely, so Pool() above never needed them).
    // Kept separate so Pool()'s Exits values stay exactly as they were, preserving the single-dimension
    // diff that Should_change_when_a_pool_weight_changes / Should_change_when_geometry_file_changes
    // rely on between Pool() and their own Template()-only comparison lists.
    private static List<ChunkPoolMember> GeneratablePool() =>
    [
        new(EntryTemplate(), 1.0f),
        new(BossTemplate(), 2.5f),
    ];

    // EntryTemplate is the only chunk with an "entry" spawn slot + Back portal, so it is always
    // the unique entry candidate. BossTemplate is a straight N/S corridor piece that also carries
    // the "boss" spawn slot + Forward portal, so it deterministically serves as every mid-path
    // chunk and the terminal/boss chunk alike.
    private static ChunkTemplate EntryTemplate()
    {
        ChunkTemplate t = Template(1, "a.obj");
        t.Exits = 0b0000_0000_0000_0010; // N-Center only
        t.SpawnSlots.Add(new ChunkSpawnSlot { Tag = "entry", LocalX = 5, LocalY = 0, LocalZ = 5 });
        t.PortalSlots.Add(new ChunkPortalSlot { Role = PortalRole.Back, LocalX = 10, LocalY = 0, LocalZ = 10 });
        return t;
    }

    private static ChunkTemplate BossTemplate()
    {
        ChunkTemplate t = Template(2, "b.obj");
        t.Exits = 0b0000_0000_1000_0010; // N-Center + S-Center
        t.SpawnSlots.Add(new ChunkSpawnSlot { Tag = "boss", LocalX = 5, LocalY = 0, LocalZ = 5 });
        t.PortalSlots.Add(new ChunkPortalSlot { Role = PortalRole.Forward, LocalX = 10, LocalY = 0, LocalZ = 10 });
        return t;
    }

    [Fact]
    public void Should_return_eight_char_lowercase_hex()
    {
        string version = LayoutConfigVersion.Compute(Config(), Pool());

        Assert.Equal(8, version.Length);
        Assert.Matches("^[0-9a-f]{8}$", version);
    }

    [Fact]
    public void Should_return_same_stamp_for_identical_inputs()
    {
        Assert.Equal(
            LayoutConfigVersion.Compute(Config(), Pool()),
            LayoutConfigVersion.Compute(Config(), Pool()));
    }

    [Fact]
    public void Should_ignore_pool_member_ordering()
    {
        List<ChunkPoolMember> reversed = Pool();
        reversed.Reverse();

        Assert.Equal(
            LayoutConfigVersion.Compute(Config(), Pool()),
            LayoutConfigVersion.Compute(Config(), reversed));
    }

    [Fact]
    public void Should_change_when_a_pool_weight_changes()
    {
        List<ChunkPoolMember> tweaked =
        [
            new(Template(1, "a.obj"), 1.0f),
            new(Template(2, "b.obj"), 9.0f),
        ];

        Assert.NotEqual(
            LayoutConfigVersion.Compute(Config(), Pool()),
            LayoutConfigVersion.Compute(Config(), tweaked));
    }

    [Fact]
    public void Should_change_when_geometry_file_changes()
    {
        List<ChunkPoolMember> tweaked =
        [
            new(Template(1, "a.obj"), 1.0f),
            new(Template(2, "DIFFERENT.obj"), 2.5f),
        ];

        Assert.NotEqual(
            LayoutConfigVersion.Compute(Config(), Pool()),
            LayoutConfigVersion.Compute(Config(), tweaked));
    }

    [Fact]
    public void Should_change_when_a_config_field_changes()
    {
        Assert.NotEqual(
            LayoutConfigVersion.Compute(Config(branchChance: 0.25f), Pool()),
            LayoutConfigVersion.Compute(Config(branchChance: 0.75f), Pool()));
    }

    /// <summary>
    /// Pins the fix in 1517f98a. Every numeric append in LayoutConfigVersion.Compute must
    /// use CultureInfo.InvariantCulture explicitly rather than the implicit ToString()
    /// overload, which formats using CultureInfo.CurrentCulture. On a de-DE host that
    /// culture uses a comma decimal separator, so BranchChance/Weight/CellSize would encode
    /// differently there than on an invariant-like (e.g. en-US) host, producing a different
    /// FNV hash for byte-for-byte identical inputs -- a permanent phantom "layout stale"
    /// banner that only reproduces on non-invariant locales. Without ToString(CultureInfo)
    /// on those floats, this test fails on any machine whose CurrentCulture is de-DE.
    /// </summary>
    [Fact]
    public void Should_return_the_same_stamp_regardless_of_current_culture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            string invariantStamp = LayoutConfigVersion.Compute(Config(), Pool());

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            string deStamp = LayoutConfigVersion.Compute(Config(), Pool());

            Assert.Equal(invariantStamp, deStamp);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// LayoutConfigVersion.Compute hashes ten ProceduralMapConfig fields and six per-member
    /// fields, but before this test only BranchChance, Weight and GeometryFile were pinned.
    /// A refactor silently dropping any of these from the encoding would pass every other
    /// test in this file while quietly disabling drift detection for that input -- exactly
    /// the failure this feature exists to catch. One field mutated at a time, everything
    /// else held fixed, so each case can only pass if that specific field is still hashed.
    /// </summary>
    public static IEnumerable<object[]> UnpinnedDriftInputs()
    {
        yield return ["SpawnTableId", (Action<ProceduralMapConfig, ChunkTemplate>)((c, _) => c.SpawnTableId = new SpawnTableId(2))];
        yield return ["BackPortalTargetMapId", (Action<ProceduralMapConfig, ChunkTemplate>)((c, _) => c.BackPortalTargetMapId = 7)];
        yield return ["ForwardPortalTargetMapId", (Action<ProceduralMapConfig, ChunkTemplate>)((c, _) => c.ForwardPortalTargetMapId = 7)];
        yield return ["Exits", (Action<ProceduralMapConfig, ChunkTemplate>)((_, t) => t.Exits = (ushort)~t.Exits)];
        yield return ["CellFootprintX", (Action<ProceduralMapConfig, ChunkTemplate>)((_, t) => t.CellFootprintX = 9)];
        yield return ["CellFootprintZ", (Action<ProceduralMapConfig, ChunkTemplate>)((_, t) => t.CellFootprintZ = 9)];
    }

    [Theory]
    [MemberData(nameof(UnpinnedDriftInputs))]
    public void Should_change_stamp_when_a_currently_unpinned_input_changes(
        string fieldName, Action<ProceduralMapConfig, ChunkTemplate> mutate)
    {
        ProceduralMapConfig baselineConfig = Config();
        List<ChunkPoolMember> baselinePool = Pool();

        ProceduralMapConfig mutatedConfig = Config();
        ChunkTemplate mutatedTemplate = Template(1, "a.obj");
        List<ChunkPoolMember> mutatedPool = [new(mutatedTemplate, 1.0f), new(Template(2, "b.obj"), 2.5f)];
        mutate(mutatedConfig, mutatedTemplate);

        string baselineStamp = LayoutConfigVersion.Compute(baselineConfig, baselinePool);
        string mutatedStamp = LayoutConfigVersion.Compute(mutatedConfig, mutatedPool);

        Assert.True(baselineStamp != mutatedStamp, $"Expected the stamp to change when {fieldName} changes, but it did not.");
    }

    [Fact]
    public void Should_be_carried_on_the_generated_layout()
    {
        var generator = new ProceduralLayoutGenerator(NullLoggerFactory.Instance);
        ProceduralMapConfig cfg = Config();

        ChunkLayout layout = generator.Generate(cfg, GeneratablePool(), seed: 12345);

        Assert.Equal(LayoutConfigVersion.Compute(cfg, GeneratablePool()), layout.ConfigVersion);
    }
}
