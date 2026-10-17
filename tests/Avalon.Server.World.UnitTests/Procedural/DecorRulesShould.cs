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
    /// One building, a 4 x 4 m body at X 20-24 (or from <paramref name="bodyMinX" />), Z 5-9, drawn by plaster walls
    /// 0.4 m thick: its -X face by walls turned a quarter (yaw 90), its -Z face by unturned walls starting 0.1 m east of
    /// it, so only the turned walls draw the -X face and a turn the wrong way leaves it bare. The square has a wall along
    /// Z = 0, or, when <paramref name="wallsAsDecor" />, a 2.2 m wall at X 14-16.2 that decor draws. Then the item under
    /// test, placed under <paramref name="building" />.
    /// </summary>
    [Theory]
    [InlineData("a wall inside the body", "Inn", "Wall_Plaster_Straight", 22f, 7f, 20f, null)]
    [InlineData("a wall 0.5 m past the body's -X face", "Inn", "Wall_Plaster_Straight", 20.5f, 7f, 20f, "Wall_Plaster_Straight")]
    [InlineData("a vine 0.3 m past it, dressing", "Inn", "Prop_Vine1", 20.483f, 7f, 20f, null)]
    [InlineData("a floor tile far outside, ground", "Inn", "Floor_Brick", 5f, 25f, 20f, null)]
    [InlineData("the body 0.5 m past every wall on its -X side", "Inn", "Wall_Plaster_Straight", 22f, 7f, 19.5f, "Inn/body")]
    [InlineData("a wall section on the square's wall", DecorRules.Walls, "avalon_wall_straight_2m", 15f, 0f, 20f, null)]
    [InlineData("a wall section a metre off it", DecorRules.Walls, "avalon_wall_straight_2m", 15f, 1f, 20f, "the square's walls")]
    [InlineData("a wall of a building with no proxy", "Shed", "Wall_Plaster_Straight", 10f, 20f, 20f, "Shed has no non-walkable proxy")]
    [InlineData("a drawn wall its one 2 m section leaves 0.2 m bare", DecorRules.Walls, "avalon_wall_straight_2m", 15f, 0f, 20f, "leave part of it bare", true)]
    public void Refuse_solid_decor_outside_its_buildings_proxy(
        string label, string building, string model, float x, float z, float bodyMinX, string? refusal, bool wallsAsDecor = false)
    {
        WallSegment wall = wallsAsDecor ? new WallSegment("Wall_S", 14f, 16.2f, -0.25f, 0.25f) : new WallSegment("Wall_S", 0f, 30f, -0.25f, 0.25f);
        var square = new TownSquare("town_test", 0, 0, [], [wall],
            [new BoxPiece("Inn", "body", Material.Collision, bodyMinX, 24f, 5f, 9f, 0f, 3.12f)], false, false, [])
        {
            WallsAsDecor = wallsAsDecor,
            Decor =
            [
                new DecorItem("Inn", "Wall_Plaster_Straight", 21.1f, 0f, 5.314f, 0),
                new DecorItem("Inn", "Wall_Plaster_Straight", 23.1f, 0f, 5.314f, 0),
                new DecorItem("Inn", "Wall_Plaster_Straight", 20.314f, 0f, 6f, 90),
                new DecorItem("Inn", "Wall_Plaster_Straight", 20.314f, 0f, 8f, 90),
                new DecorItem(building, model, x, 0f, z, 0),
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
