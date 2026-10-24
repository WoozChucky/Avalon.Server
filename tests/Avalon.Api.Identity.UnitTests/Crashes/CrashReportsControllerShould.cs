using System.Net;
using System.Text.Json;
using Avalon.Crashes.Queries;
using Avalon.Crashes;
using NSubstitute;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.Crashes;

public class CrashReportsControllerShould
{
    [Fact]
    public async Task Return_unavailable_for_missing_report_and_not_found_for_missing_group()
    {
        await using CrashApiFixture fixture = await CrashApiFixture.StartAsync();
        using HttpResponseMessage report = await fixture.Host.Client.GetAsync($"/admin/crashes/reports/{CrashApiFixture.ReportId.Value}");
        Assert.Equal(HttpStatusCode.NotFound, report.StatusCode);
        Assert.True(report.Headers.CacheControl?.NoStore);
        Assert.Contains("CRASH_REPORT_NOT_AVAILABLE", await report.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using HttpResponseMessage group = await fixture.Host.Client.GetAsync($"/admin/crashes/groups/{CrashApiFixture.GroupId.Value}");
        Assert.Equal(HttpStatusCode.NotFound, group.StatusCode);
        Assert.Contains("CRASH_GROUP_NOT_FOUND", await group.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Expose_typed_bounded_report_only_with_finite_enums_and_hex_addresses()
    {
        await using CrashApiFixture fixture = await CrashApiFixture.StartAsync();
        var analysis = new CrashAnalysis(1, new CrashIdentity("EXCEPTION_ACCESS_VIOLATION", 0xc0000005, "0x0000000000000010", 7, 8),
            new CrashSystem("Windows", "10.0.123", "123", "x86_64", null),
            [new CrashThread(7, 8, [new CrashFrame("runtime.exe", "0x0000000000000020", "Fault", null, null, [], "future-native-trust")])],
            [new CrashModule("runtime.exe", "identity", null, null, "1.0.1", "0x0000000000001000", "0x0000000000002000", false)],
            new CrashMetadata(1, "runtime.exe", "1.0.1", "abc", 0xc0000005, "0x0000000000000010", 8, CrashApiFixture.Now),
            null, CrashQualityFlags.MissingSystemSymbols, new ProcessorIdentity("0.27.0", "2.3.9", "1"));
        fixture.Reads.GetReportAsync(Arg.Any<CrashReportId>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new CrashReportDetail(CrashApiFixture.ReportId, CrashReportStatus.Processed, (CrashFailureCode)999,
                CrashApiFixture.Now.ToOffset(TimeSpan.FromHours(1)), CrashApiFixture.Now.AddDays(90), CrashApiFixture.Now,
                CrashApiFixture.GroupId, "1.0.1", "abc", CrashApiFixture.Now, 0xc0000005, "0x0000000000000010", "runtime.exe",
                "10.0.123", "123", "x86_64", null, null, null, CrashQualityFlags.MissingSystemSymbols, analysis, "bounded log text"));
        using HttpResponseMessage response = await fixture.Host.Client.GetAsync($"/admin/crashes/reports/{CrashApiFixture.ReportId.Value}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement report = json.RootElement;
        Assert.Equal(CrashApiFixture.ReportId.Value, report.GetProperty("id").GetGuid());
        Assert.Equal("processed", report.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, report.GetProperty("reason").ValueKind);
        Assert.Equal("bounded log text", report.GetProperty("logTail").GetString());
        Assert.Equal(TimeSpan.Zero, report.GetProperty("receivedAt").GetDateTimeOffset().Offset);
        Assert.Equal("0x0000000000000010", report.GetProperty("exceptionAddress").GetString());
        JsonElement typed = report.GetProperty("analysis");
        Assert.Equal("0.27.0", typed.GetProperty("processor").GetProperty("stackwalkVersion").GetString());
        Assert.Equal(JsonValueKind.Null, typed.GetProperty("threads")[0].GetProperty("frames")[0].GetProperty("trust").ValueKind);
        Assert.False(typed.TryGetProperty("logTail", out _));
        fixture.Clock.Advance(TimeSpan.FromDays(90));
        using HttpResponseMessage expired = await fixture.Host.Client.GetAsync($"/admin/crashes/reports/{CrashApiFixture.ReportId.Value}");
        Assert.Equal(HttpStatusCode.NotFound, expired.StatusCode);
        Assert.Contains("CRASH_REPORT_NOT_AVAILABLE", await expired.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
