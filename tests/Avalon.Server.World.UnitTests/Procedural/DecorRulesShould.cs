using Avalon.ChunkGen;

namespace Avalon.Server.World.UnitTests.Procedural;

/// <summary>
/// What a player sees and what they collide with agree: a solid kit piece stands inside its building's collision proxy,
/// and the proxy reaches no further than the art on the camera's (-X and -Z) sides, so there is neither a wall to walk
/// through nor an invisible one to bump.
/// </summary>
public class DecorRulesShould
{
    /// <summary>
    /// One building, a 4 x 4 m body at X 20-24 (or from <paramref name="bodyMinX" />), Z 5-9, its -X and -Z faces drawn
    /// by plaster walls 0.4 m thick, plus the item under test.
    /// </summary>
    [Theory]
    [InlineData("a wall inside the body", "Wall_Plaster_Straight", 22f, 7f, 20f, null)]
    [InlineData("a wall 0.5 m past the body's -X face", "Wall_Plaster_Straight", 20.5f, 7f, 20f, "Wall_Plaster_Straight")]
    [InlineData("a vine 0.3 m past it, dressing", "Prop_Vine1", 20.483f, 7f, 20f, null)]
    [InlineData("a floor tile far outside, ground", "Floor_Brick", 5f, 25f, 20f, null)]
    [InlineData("the body 0.5 m past every wall on its -X side", "Wall_Plaster_Straight", 22f, 7f, 19.5f, "Inn/body")]
    public void Refuse_solid_decor_outside_its_buildings_proxy(string label, string model, float x, float z, float bodyMinX, string? refusal)
    {
        var square = new TownSquare("town_test", 0, 0, [], [],
            [new BoxPiece("Inn", "body", Material.Collision, bodyMinX, 24f, 5f, 9f, 0f, 3.12f)], false, false, [])
        {
            Decor =
            [
                new DecorItem("Inn", "Wall_Plaster_Straight", 21f, 0f, 5.314f, 0),
                new DecorItem("Inn", "Wall_Plaster_Straight", 23f, 0f, 5.314f, 0),
                new DecorItem("Inn", "Wall_Plaster_Straight", 20.314f, 0f, 6f, 90),
                new DecorItem("Inn", "Wall_Plaster_Straight", 20.314f, 0f, 8f, 90),
                new DecorItem("Inn", model, x, 0f, z, 0),
            ],
        };

        Exception? thrown = Record.Exception(square.Validate);

        if (refusal is null)
        {
            Assert.True(thrown is null, $"{label}: {thrown?.Message}");
        }
        else
        {
            InvalidOperationException refused = Assert.IsType<InvalidOperationException>(thrown);
            Assert.True(refused.Message.Contains(refusal, StringComparison.Ordinal), $"{label}: {refused.Message}");
        }
    }
}
