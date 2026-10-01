using Avalon.Common.Mathematics;
using Avalon.World.ChunkLayouts;
using Xunit;

namespace Avalon.Server.World.UnitTests.Procedural;

/// <summary>A 2x2 group turned as a whole, in ChunkRotation's sense: rotation 1 takes north to east.</summary>
public class ChunkGroupRotationShould
{
    [Theory]
    [InlineData(0, 0, 1, 0, 1)]   // SW -> NW
    [InlineData(0, 1, 1, 1, 1)]   // NW -> NE
    [InlineData(1, 1, 1, 1, 0)]   // NE -> SE
    [InlineData(1, 0, 1, 0, 0)]   // SE -> SW
    [InlineData(0, 0, 2, 1, 1)]
    [InlineData(1, 0, 3, 1, 1)]
    public void Turn_a_cell_of_a_two_by_two_group(int x, int z, byte rotation, int expectedX, int expectedZ) =>
        Assert.Equal((expectedX, expectedZ), ChunkGroupRotation.RotateCell(x, z, 2, 2, rotation));

    [Fact]
    public void Swap_the_sides_of_a_non_square_group_on_a_quarter_turn() =>
        Assert.Equal((0, 2), ChunkGroupRotation.RotateCell(0, 0, 3, 1, 1));   // the west end of a 3x1 row goes north

    /// <summary>
    /// The client and the bake rotate each chunk about its own centre (ChunkRotation.LocalToWorld). A group turned by
    /// moving each member's cell with RotateCell and giving it the same rotation must put every point where turning
    /// the whole 60x60 group about its centre would.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Agree_with_rotating_the_whole_group_about_its_centre(byte rotation)
    {
        const float cell = 30f;
        foreach ((int x, int z) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) })
        {
            (int rx, int rz) = ChunkGroupRotation.RotateCell(x, z, 2, 2, rotation);
            foreach ((float lx, float lz) in new[] { (3f, 7f), (29f, 1f), (15f, 22f) })
            {
                Vector3 member = ChunkRotation.LocalToWorld(lx, 0f, lz, rotation, cell, new Vector3(rx * cell, 0f, rz * cell));
                Vector3 whole = ChunkRotation.LocalToWorld(x * cell + lx, 0f, z * cell + lz, rotation, 2 * cell, Vector3.zero);

                Assert.Equal(whole.x, member.x, 3);
                Assert.Equal(whole.z, member.z, 3);
            }
        }
    }
}
