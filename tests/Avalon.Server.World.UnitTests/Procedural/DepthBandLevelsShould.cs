using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using Xunit;

namespace Avalon.Server.World.UnitTests.Procedural;

public class DepthBandLevelsShould
{
    private static readonly List<ProceduralDepthBand> Forest =
    [
        new() { MinDepth = 1, MaxDepth = 3, MinLevel = 1, MaxLevel = 3 },
        new() { MinDepth = 4, MaxDepth = 7, MinLevel = 3, MaxLevel = 6 },
        new() { MinDepth = 8, MaxDepth = null, MinLevel = 5, MaxLevel = 8 },
    ];

    [Theory]
    [InlineData(1, 1, 3)]
    [InlineData(3, 1, 3)]
    [InlineData(4, 3, 6)]
    [InlineData(7, 3, 6)]
    [InlineData(8, 5, 8)]
    [InlineData(40, 5, 8)]
    public void Find_the_band_a_depth_falls_in(int depth, int min, int max) =>
        Assert.Equal(new LevelRange((ushort)min, (ushort)max), DepthBandLevels.For(Forest, depth, setPiece: false));

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void Put_every_set_piece_in_the_highest_band(int depth) =>
        Assert.Equal(new LevelRange(5, 8), DepthBandLevels.For(Forest, depth, setPiece: true));

    [Fact]
    public void Leave_a_depth_no_band_covers_to_the_template() => Assert.Null(DepthBandLevels.For(Forest, 0, setPiece: false));

    [Fact]
    public void Leave_everything_to_the_template_when_there_are_no_bands()
    {
        Assert.Null(DepthBandLevels.For([], 5, setPiece: true));
        Assert.Null(DepthBandLevels.BossLevel([]));
    }

    [Fact]
    public void Put_the_boss_at_the_top_of_the_highest_band() => Assert.Equal((ushort)8, DepthBandLevels.BossLevel(Forest));

    [Fact]
    public void Accept_the_forest_bands() => Assert.Null(DepthBandLevels.Problem(Forest));

    [Fact]
    public void Refuse_overlapping_bands() => Assert.NotNull(DepthBandLevels.Problem(
    [
        new() { MinDepth = 1, MaxDepth = 4, MinLevel = 1, MaxLevel = 3 },
        new() { MinDepth = 4, MaxDepth = null, MinLevel = 3, MaxLevel = 6 },
    ]));

    [Fact]
    public void Refuse_an_open_band_below_another() => Assert.NotNull(DepthBandLevels.Problem(
    [
        new() { MinDepth = 1, MaxDepth = null, MinLevel = 1, MaxLevel = 3 },
        new() { MinDepth = 4, MaxDepth = 7, MinLevel = 3, MaxLevel = 6 },
    ]));

    [Fact]
    public void Refuse_a_band_whose_levels_run_backwards() => Assert.NotNull(DepthBandLevels.Problem(
        [new() { MinDepth = 1, MaxDepth = null, MinLevel = 6, MaxLevel = 3 }]));

    [Fact]
    public void Refuse_a_band_at_level_zero() => Assert.NotNull(DepthBandLevels.Problem(
        [new() { MinDepth = 1, MaxDepth = null, MinLevel = 0, MaxLevel = 3 }]));

    [Fact]
    public void Refuse_a_band_that_starts_below_depth_zero() => Assert.Equal("band from depth -1 starts below 0",
        DepthBandLevels.Problem([new() { MinDepth = -1, MaxDepth = 3, MinLevel = 1, MaxLevel = 3 }]));

    [Fact]
    public void Refuse_a_band_that_ends_before_it_starts() => Assert.Equal("band from depth 4 ends at depth 2",
        DepthBandLevels.Problem([new() { MinDepth = 4, MaxDepth = 2, MinLevel = 1, MaxLevel = 3 }]));

    /// <summary>A gap between bands is allowed: a depth in it rolls from the creature template's own range.</summary>
    [Fact]
    public void Accept_a_gap_between_bands_and_leave_a_depth_in_it_to_the_template()
    {
        List<ProceduralDepthBand> gapped =
        [
            new() { MinDepth = 1, MaxDepth = 3, MinLevel = 1, MaxLevel = 3 },
            new() { MinDepth = 6, MaxDepth = null, MinLevel = 4, MaxLevel = 6 },
        ];

        Assert.Null(DepthBandLevels.Problem(gapped));
        Assert.Null(DepthBandLevels.For(gapped, 4, setPiece: false));
        Assert.Equal(new LevelRange(4, 6), DepthBandLevels.For(gapped, 6, setPiece: false));
    }
}
