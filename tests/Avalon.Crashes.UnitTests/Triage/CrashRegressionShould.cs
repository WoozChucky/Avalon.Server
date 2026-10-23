using Avalon.Crashes.Triage;
using Xunit;

namespace Avalon.Crashes.UnitTests.Triage;

public sealed class CrashRegressionShould
{
    [Theory]
    [InlineData(CrashGroupStatus.Fixed, "1.2.3", "1.2.3", 0, true)]
    [InlineData(CrashGroupStatus.Fixed, "1.2.3", "1.2.3+build.2", 1, true)]
    [InlineData(CrashGroupStatus.Fixed, "1.2.3", "1.2.2", 1, false)]
    [InlineData(CrashGroupStatus.Fixed, "1.2.3", "1.2.3-rc.1", 1, false)]
    [InlineData(CrashGroupStatus.Fixed, "1.2.3-rc.1", "1.2.3", 1, true)]
    [InlineData(CrashGroupStatus.Fixed, "1.2.3", "2.0.0", -1, false)]
    [InlineData(CrashGroupStatus.Fixed, "1.2.3", "unversioned", 1, false)]
    [InlineData(CrashGroupStatus.Fixed, null, "2.0.0", 1, false)]
    [InlineData(CrashGroupStatus.New, "1.2.3", "2.0.0", 1, false)]
    [InlineData(CrashGroupStatus.Investigating, "1.2.3", "2.0.0", 1, false)]
    [InlineData(CrashGroupStatus.Ignored, "1.2.3", "2.0.0", 1, false)]
    public void Should_reopen_only_a_fresh_fixed_group_at_or_above_the_fix(CrashGroupStatus status, string? fixedVersion, string reportVersion, int receivedMinutesAfterFix, bool expected)
    {
        var fixedAt = new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, CrashRegression.ShouldReopen(status, fixedVersion, fixedAt, reportVersion, fixedAt.AddMinutes(receivedMinutesAfterFix)));
    }
}
