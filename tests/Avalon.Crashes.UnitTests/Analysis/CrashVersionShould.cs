using Avalon.Crashes.Analysis;
using Xunit;

namespace Avalon.Crashes.UnitTests.Analysis;

public sealed class CrashVersionShould
{
    [Theory]
    [InlineData("0.4.1-dev.10", "0.4.1-dev.2", 1)]
    [InlineData("0.4.1+aaa", "0.4.1+bbb", 0)]
    [InlineData("1.0.0-alpha", "1.0.0", -1)]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta", -1)]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1", -1)]
    [InlineData("999999999999999999999.0.0", "2.0.0", 1)]
    public void Should_compare_prerelease_versions_numerically(string left, string right, int expected) =>
        Assert.Equal(expected, CrashVersion.Compare(left, right));

    [Theory]
    [InlineData("0.4")]
    [InlineData("v1.2.3")]
    [InlineData("01.2.3")]
    [InlineData("1.2.3-01")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3+abc..def")]
    [InlineData("1.2.3\n")]
    [InlineData("1.2.3-β")]
    public void Should_refuse_invalid_strict_semver(string invalid)
    {
        Assert.Null(CrashVersion.Compare(invalid, "0.4.1"));
        Assert.Null(CrashVersion.Compare("0.4.1", invalid));
    }
}
