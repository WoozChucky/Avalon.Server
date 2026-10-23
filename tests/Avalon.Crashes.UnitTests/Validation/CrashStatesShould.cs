using System.Text.Json;
using Xunit;

namespace Avalon.Crashes.UnitTests.Validation;

public sealed class CrashStatesShould
{
    [Fact]
    public void Should_keep_stable_state_and_quality_strings_and_refuse_undefined_numbers()
    {
        Assert.Equal("\"retry_wait\"", JsonSerializer.Serialize(CrashReportStatus.RetryWait));
        Assert.Equal("\"fixed\"", JsonSerializer.Serialize(CrashGroupStatus.Fixed));
        Assert.Equal("\"archive_too_large\"", JsonSerializer.Serialize(CrashFailureCode.ArchiveTooLarge));
        Assert.Equal("\"missingSystemSymbols, truncated\"", JsonSerializer.Serialize(CrashQualityFlags.MissingSystemSymbols | CrashQualityFlags.Truncated));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize((CrashReportStatus)1000));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CrashReportStatus>("1"));
    }
}
