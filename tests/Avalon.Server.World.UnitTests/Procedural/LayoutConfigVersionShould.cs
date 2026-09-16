using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;

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
}
