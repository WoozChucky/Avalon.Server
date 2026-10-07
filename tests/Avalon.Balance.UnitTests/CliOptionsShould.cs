using Avalon.World.Public.Enums;
using Xunit;

namespace Avalon.Balance.UnitTests;

public class CliOptionsShould
{
    [Fact]
    public void Read_every_option()
    {
        var o = CliOptions.Parse(["--class", "warrior", "--scenario", "normal-3", "--runs", "5000", "--seed", "1",
            "--overrides", "balance/try-slam.json", "--out", "tmp/out"]);

        Assert.Equal(new CliOptions(CharacterClass.Warrior, "normal-3", 5000, 1, "balance/try-slam.json", "tmp/out", false), o);
    }

    [Fact]
    public void Default_to_nothing() => Assert.Equal(new CliOptions(null, null, null, null, null, null, false), CliOptions.Parse([]));

    [Fact]
    public void Refuse_an_unknown_class() =>
        Assert.Contains("Unknown class 'Paladin'",
            Assert.Throws<ArgumentException>(() => CliOptions.Parse(["--class", "Paladin"])).Message, StringComparison.Ordinal);

    [Theory]
    [InlineData("--runs", "many")]
    [InlineData("--runs", "0")]
    [InlineData("--seed", "x")]
    public void Refuse_a_bad_number(string option, string value) =>
        Assert.Throws<ArgumentException>(() => CliOptions.Parse([option, value]));

    [Fact]
    public void Refuse_an_unknown_option_and_a_missing_value()
    {
        Assert.Throws<ArgumentException>(() => CliOptions.Parse(["--verbose"]));
        Assert.Throws<ArgumentException>(() => CliOptions.Parse(["--class"]));
    }

    [Fact]
    public void Ask_for_help() => Assert.True(CliOptions.Parse(["-h"]).Help);
}
