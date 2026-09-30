using Avalon.Balance.Data;
using Avalon.World.Creatures;
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
    public void Read_each_forest_scripts_kit_by_name(string script, uint basic)
    {
        CreatureAbilityKit? kit = CreatureKits.For(script);

        Assert.NotNull(kit);
        Assert.Equal(basic, kit.Basic.Value);
    }

    [Fact]
    public void Find_no_kit_for_a_town_npc() => Assert.Null(CreatureKits.For("TownNpcScript"));

    [Fact]
    public void Keep_blight_spit_out_of_melee() => Assert.Equal([305u], CreatureKits.RangedOnly);
}
