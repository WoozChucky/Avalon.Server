using Avalon.ChunkGen;

namespace Avalon.Server.World.UnitTests.Procedural;

/// <summary>
/// A building recipe placed in a square: its kit pieces and its collision proxies turn together about the anchor, in
/// ChunkRotation's sense (rotation 1 takes north to east), so what the client draws stays where the navmesh blocks.
/// </summary>
public class TownBuildingShould
{
    [Theory]
    [InlineData(0, 12f, 21f, 0, 10f, 14f, 20f, 22f, Facing.NegX)]
    [InlineData(1, 11f, 18f, 90, 10f, 12f, 16f, 20f, Facing.PosZ)]
    [InlineData(2, 8f, 19f, 180, 6f, 10f, 18f, 20f, Facing.PosX)]
    [InlineData(3, 9f, 22f, 270, 8f, 10f, 20f, 24f, Facing.NegZ)]
    public void Turn_pieces_and_proxies_together(int rotation, float pieceX, float pieceZ, int yaw,
        float minX, float maxX, float minZ, float maxZ, Facing front)
    {
        var recipe = new BuildingRecipe("test",
            [new DecorItem("", "Wall_Plaster_Straight", 2f, 0f, 1f, 0)],
            [new BoxPiece("", "body", Material.Plaster, 0f, 4f, 0f, 2f, 0f, 3f) { Front = Facing.NegX }]);

        (IReadOnlyList<TownPiece> proxies, IReadOnlyList<DecorItem> decor) = TownBuilding.Place(recipe, "Inn", 10f, 20f, rotation);

        Assert.Equal(new DecorItem("Inn", "Wall_Plaster_Straight", pieceX, 0f, pieceZ, yaw), Assert.Single(decor));
        BoxPiece box = Assert.IsType<BoxPiece>(Assert.Single(proxies));
        Assert.Equal((minX, maxX, minZ, maxZ), box.Bounds);
        Assert.Equal((front, Material.Collision, "Inn"), (box.Front, box.Material, box.Building));
    }

    /// <summary>
    /// A model the bounds table does not have; and a square gable, whose ridge runs along X however the recipe turns
    /// (GablePiece.RidgeAlongX), so the roof would not turn with the building.
    /// </summary>
    [Theory]
    [InlineData("Wall_Nope", 0f, "Wall_Nope")]
    [InlineData("Wall_Plaster_Straight", 4f, "Inn/roof")]
    public void Refuse_what_it_cannot_place(string model, float gableDepth, string expected)
    {
        List<TownPiece> proxies = [new BoxPiece("", "body", Material.Plaster, 0f, 4f, 0f, 2f, 0f, 3f) { Front = Facing.NegX }];
        if (gableDepth > 0f) proxies.Add(new GablePiece("", "roof", Material.Roof, 0f, 4f, 0f, gableDepth, 3f, 5f));
        var recipe = new BuildingRecipe("test", [new DecorItem("", model, 0f, 0f, 0f, 0)], proxies);

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() => TownBuilding.Place(recipe, "Inn", 10f, 20f, 1));
        Assert.Contains(expected, refusal.Message, StringComparison.Ordinal);
    }
}
