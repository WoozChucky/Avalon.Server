using Avalon.Balance.Core;
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
}
