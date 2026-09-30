using Avalon.Balance.Data;
using Avalon.Common.ValueObjects;
using Xunit;

namespace Avalon.Balance.UnitTests;

public class CreatureKitsShould
{
    [Theory]
    [InlineData("ThornbackBoarScript", 300u)]
    [InlineData("GreyFenWolfScript", 302u)]
    [InlineData("BlightflySwarmlingScript", 304u)]
    [InlineData("HuskOfTheWoldScript", 306u)]
    [InlineData("BramblemawAlphaScript", 308u)]
    [InlineData("OldTuskrootScript", 311u)]
    [InlineData("MotherBrambleScript", 314u)]
    public void Hold_each_forest_scripts_kit_by_name(string script, uint basic)
    {
        Assert.True(CreatureKits.ByScript.TryGetValue(script, out CreatureKit? kit));
        Assert.Equal(basic, kit.Basic.Value);
    }

    [Fact]
    public void Hold_seven_kits() => Assert.Equal(7, CreatureKits.ByScript.Count);

    [Fact]
    public void Hold_no_kit_for_a_town_npc() => Assert.False(CreatureKits.ByScript.ContainsKey("TownNpcScript"));

    [Fact]
    public void Keep_blight_spit_out_of_melee() =>
        Assert.Equal([new AbilityId(305)], CreatureKits.ByScript["BlightflySwarmlingScript"].RangedOnly);
}
