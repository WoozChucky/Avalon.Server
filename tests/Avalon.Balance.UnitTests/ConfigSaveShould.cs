using Avalon.Balance.Core;
using Xunit;

namespace Avalon.Balance.UnitTests;

public class ConfigSaveShould
{
    [Fact]
    public void Round_trip_the_checked_in_files()
    {
        BalanceConfig config = TestData.Config();
        (string s, string t, string r) = ConfigFiles.Save(config);

        BalanceConfig again = new(ConfigFiles.ParseScenarios(s), ConfigFiles.ParseTargets(t), ConfigFiles.ParseRotations(r));
        Assert.Equal(ConfigFiles.Save(again), (s, t, r));
    }

    [Fact]
    public void Write_indented_camel_case_with_a_trailing_newline()
    {
        (string s, string t, string r) = ConfigFiles.Save(TestData.Config());

        foreach (string text in new[] { s, t, r })
        {
            Assert.EndsWith("}\n", text);
            Assert.DoesNotContain("\r", text);
        }

        Assert.Contains("\n  \"runs\":", s);
        Assert.Contains("\"gradedGear\":", t);
        Assert.DoesNotContain("\"offset\"", s);
    }
}
