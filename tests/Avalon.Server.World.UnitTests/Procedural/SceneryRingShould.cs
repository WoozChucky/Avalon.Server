using System.Globalization;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.World.ChunkLayouts;

namespace Avalon.Server.World.UnitTests.Procedural;

public class SceneryRingShould
{
    /// <summary>
    /// Review Focus 1. Every chunk's open exits face ring cell (1, 1); the cap there must be of exactly the shape the
    /// exits make, turned onto the sides they come through.
    /// </summary>
    /// <param name="chunks">Layout cells and the one side each has an exit on, as <c>"x,z side; ..."</c>.</param>
    /// <param name="expected">The capped sides at (1, 1), as letters in N, E, S, W order.</param>
    [Theory]
    [InlineData("one side", "1,0 N", "S")]
    [InlineData("two adjacent", "1,0 N; 0,1 E", "SW")]
    [InlineData("two adjacent, turned", "0,1 E; 1,2 S", "NW")]
    [InlineData("two opposite", "1,0 N; 1,2 S", "NS")]
    [InlineData("three", "1,0 N; 0,1 E; 1,2 S", "NSW")]
    [InlineData("four", "1,0 N; 0,1 E; 1,2 S; 2,1 W", "NESW")]
    public void Cap_a_cell_faced_from_several_sides_with_a_piece_of_that_shape(string row, string chunks, string expected)
    {
        _ = row;
        SceneryPool pool = SceneryTestPool.WithoutTownWard;
        var exits = new Dictionary<ChunkTemplateId, ushort>();
        var placed = new List<PlacedChunk>();
        foreach (string chunk in chunks.Split(';', StringSplitOptions.TrimEntries))
        {
            string[] parts = chunk.Split([',', ' ']);
            short x = short.Parse(parts[0], CultureInfo.InvariantCulture);
            short z = short.Parse(parts[1], CultureInfo.InvariantCulture);
            ExitSide side = Enum.Parse<ExitSide>(parts[2]);
            var id = new ChunkTemplateId(placed.Count + 1);
            exits[id] = (ushort)(1 << ((int)side * 3 + (int)ExitSlot.Center));
            placed.Add(new PlacedChunk(id, x, z, 0, new Vector3(x * 30f, 0f, z * 30f)));
        }

        var layout = new ChunkLayout(7, placed, placed[0], null, [], Vector3.zero, 30f);

        SceneryPlacement cap = SceneryRing.Build(layout, id => exits[id], pool).Single(p => (p.GridX, p.GridZ) == (1, 1));
        SceneryPoolPiece piece = pool.Pieces.Single(p => p.Name == cap.Name);
        Assert.Equal(SceneryKind.Cap, piece.Kind);
        Assert.Equal(expected, ScenerySides.Name(ScenerySides.Rotate(piece.CappedSides, cap.Rotation)));
    }
}
