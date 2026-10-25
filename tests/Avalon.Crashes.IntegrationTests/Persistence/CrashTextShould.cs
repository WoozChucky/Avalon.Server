using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Avalon.Api.Identity;
using Avalon.Api.Testing;
using Avalon.Common.Accounts;
using Avalon.Crashes.Analysis;
using Avalon.Crashes.Persistence;
using Avalon.Crashes.UnitTests;
using Avalon.Crashes.Validation;
using Avalon.Database.Crashes;
using Avalon.Database.Crashes.Entities;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Crashes.IntegrationTests.Persistence;

[Trait("Category", "CrashesPostgres")]
public sealed class CrashTextShould
{
    [IntegrationTheory]
    [InlineData("log")]
    [InlineData("native")]
    [InlineData("safe")]
    public async Task Should_persist_decoded_text_once_without_an_abandoned_lease(string route)
    {
        await using var database = new VerticalDatabase();
        await database.InitializeAsync();
        string directory = Path.Combine(Path.GetTempPath(), "crash-text-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var zip = new MemoryStream(CrashFixture.Archive(log: Encoding.UTF8.GetBytes(route == "log" ? "before\0after" : @"literal\u0000")));
            CrashInput input = await new CrashArchiveReader().ReadAsync(zip, CrashFixture.Version, directory, default);
            CrashAnalysis analysis = Project(input, route == "native" ? "before\0after" : @"literal\u0000");
            var queue = new CrashQueue(database.Factory);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var source = new StoredCrashObject($"{CrashFixture.Version}/{now:yyyy-MM-dd}/{Guid.NewGuid():N}.zip", "etag", 1024, now);
            Assert.True(await queue.DiscoverAsync(source, now, default));
            CrashLease lease = (await queue.ClaimAsync(now, default))!;
            var result = CrashAttemptResult.Processed(analysis, CrashSignature.Create(analysis));
            // Persist first: the regression must expose PostgreSQL 22021, not merely a string mismatch.
            Assert.True(await queue.CompleteAsync(lease, result, now, default));
            Assert.True(await queue.CompleteAsync(lease, result, now, default));
            Assert.Null(await queue.ClaimAsync(now.AddMinutes(11), default));
            await using CrashesDbContext db = database.Factory.CreateDbContext();
            CrashReport report = await db.Reports.SingleAsync();
            Assert.Equal(CrashReportStatus.Processed, report.Status);
            Assert.Null(report.LeaseOwner);
            Assert.Equal(1, report.AttemptCount);
            Assert.Equal(0, report.ConsecutiveToolFailures);
            Assert.Equal(1, (await db.Groups.SingleAsync()).ReportCount);
            Assert.Equal(route == "log" ? "before\ufffdafter" : @"literal\u0000", report.LogTail);
            Assert.Equal(route == "native" ? "before\ufffdafter" : @"literal\u0000", analysis.System.OsVersion);
            Assert.Equal(route != "safe", analysis.Quality.HasFlag(CrashQualityFlags.SanitizedText));
            Assert.DoesNotContain('\0', JsonSerializer.Deserialize<CrashAnalysis>(report.Stack!)!.System.OsVersion!);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [IntegrationFact]
    public async Task Should_reject_nul_notes_over_http_without_changing_triage()
    {
        await using var database = new VerticalDatabase();
        await database.InitializeAsync();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var input = new CrashInput("unused", new(1, "runtime", "1.2.3", "abc", 0xc0000005, "0x10", 42, now), null,
            new("runtime.exe", "IMAGE", "runtime.pdb", "PDB"), CrashQualityFlags.None)
        { Modules = [new("runtime.exe", "IMAGE", "runtime.pdb", "PDB", "1.2.3", 0, 4096)] };
        CrashAnalysis analysis = Project(input, "normal");
        var queue = new CrashQueue(database.Factory);
        await queue.DiscoverAsync(new($"1.2.3/{now:yyyy-MM-dd}/{Guid.NewGuid():N}.zip", "etag", 1024, now), now, default);
        await queue.CompleteAsync((await queue.ClaimAsync(now, default))!, CrashAttemptResult.Processed(analysis, CrashSignature.Create(analysis)), now, default);
        await using CrashesDbContext db = database.Factory.CreateDbContext();
        CrashGroup group = await db.Groups.SingleAsync();
        await using ApiTestHost api = await ApiTestHost.StartAsync([IdentityApi.Service], new ApiTestHostOptions
        {
            Settings = new Dictionary<string, string?> { ["Application:Crashes:Enabled"] = "true", ["Database:Crashes:ConnectionString"] = database.Identity }
        });
        Account account = ApiTestHost.MakeAccount(AccountAccessLevel.Admin);
        api.AccountNowIs(account);
        api.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTestHost.Mint(account));
        using HttpResponseMessage bad = await api.Client.PatchAsync($"/admin/crashes/groups/{group.Id.Value}",
            new StringContent("""{"triageRevision":0,"notes":"before\u0000after"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.True(bad.Headers.CacheControl?.NoStore);
        Assert.Contains("CRASH_INVALID_INPUT", await bad.Content.ReadAsStringAsync());
        await db.Entry(group).ReloadAsync();
        Assert.Equal(0, group.TriageRevision);
        Assert.Null(group.Notes);
        using HttpResponseMessage good = await api.Client.PatchAsync($"/admin/crashes/groups/{group.Id.Value}",
            new StringContent("""{"triageRevision":0,"notes":"literal\\u0000"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, good.StatusCode);
        await db.Entry(group).ReloadAsync();
        Assert.Equal(@"literal\u0000", group.Notes);
        Assert.Equal(1, group.TriageRevision);
    }

    private static CrashAnalysis Project(CrashInput input, string text) => StackwalkProjector.Project(JsonSerializer.SerializeToUtf8Bytes(new
    {
        crash_info = new { type = "EXCEPTION_ACCESS_VIOLATION", crashing_thread = 0 },
        system_info = new { os = "Windows NT", os_ver = text },
        threads = new[] { new { thread_id = 42, frames = new[] { new { offset = "0x10", module = "runtime.exe", function = "Game::Run", trust = "context" } } } }
    }), input, new("0.27.0", "2.3.9", "v1"));
}
