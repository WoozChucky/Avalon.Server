using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;

namespace Avalon.Server.World.UnitTests.Procedural;

public class DepthBandLevelsShould
{
    private static readonly List<ProceduralDepthBand> s_forest =
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
        Assert.Equal(new LevelRange((ushort)min, (ushort)max), DepthBandLevels.For(s_forest, depth, setPiece: false));

    public static TheoryData<string, ProceduralDepthBand[]> BrokenBands() => new()
    {
        { "overlapping", [new() { MinDepth = 1, MaxDepth = 4, MinLevel = 1, MaxLevel = 3 }, new() { MinDepth = 4, MaxDepth = null, MinLevel = 3, MaxLevel = 6 }] },
        { "an open band below another", [new() { MinDepth = 1, MaxDepth = null, MinLevel = 1, MaxLevel = 3 }, new() { MinDepth = 4, MaxDepth = 7, MinLevel = 3, MaxLevel = 6 }] },
        { "levels running backwards", [new() { MinDepth = 1, MaxDepth = null, MinLevel = 6, MaxLevel = 3 }] },
        { "level zero", [new() { MinDepth = 1, MaxDepth = null, MinLevel = 0, MaxLevel = 3 }] },
        { "a start below depth zero", [new() { MinDepth = -1, MaxDepth = 3, MinLevel = 1, MaxLevel = 3 }] },
        { "an end before the start", [new() { MinDepth = 4, MaxDepth = 2, MinLevel = 1, MaxLevel = 3 }] },
    };

    [Theory]
    [MemberData(nameof(BrokenBands))]
    public void Refuse_bands_that_break_a_rule(string rule, ProceduralDepthBand[] bands) =>
        Assert.True(DepthBandLevels.Problem([.. bands]) is not null, $"bands with {rule} were accepted");

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
