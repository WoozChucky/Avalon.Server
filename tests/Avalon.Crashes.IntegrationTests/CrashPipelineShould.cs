using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Amazon.S3;
using Avalon.Api.Testing;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Crashes.IntegrationTests.Storage;
using Avalon.Database.Crashes;
using Avalon.Database.Crashes.Entities;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace Avalon.Crashes.IntegrationTests;

[Trait("Category", "CrashesVertical")]
public sealed class CrashPipelineShould(ITestOutputHelper output)
{
    [IntegrationFact]
    public async Task Should_process_one_zip_into_one_admin_readable_group()
    {
        await using var fixture = new CrashPipelineFixture();
        await fixture.InitializeAsync();
        StoredCrashObject source = await fixture.UploadAsync();
        fixture.StartWorker();
        CrashReport report = await fixture.WaitReportAsync(source.Key, r => r.Status == CrashReportStatus.Processed);
        await fixture.StopWorkerAsync();
        Guid group = report.GroupId!.Value;
        JsonElement detail = await fixture.GetAsync($"/admin/crashes/reports/{report.Id.Value}");
        Assert.Equal(CrashPipelineFixture.LogCanary, detail.GetProperty("logTail").GetString());
        Assert.Contains("Application.cpp", detail.GetProperty("analysis").GetRawText());
        Assert.Contains("main.cpp", detail.GetProperty("analysis").GetRawText());
        Assert.Contains("\"cfi\"", detail.GetProperty("analysis").GetRawText());
        Assert.Equal(90, (report.ExpiresAt - report.ReceivedAt).TotalDays);
        JsonElement listed = await fixture.GetAsync("/admin/crashes/groups");
        Assert.Equal(1, listed.GetProperty("total").GetInt64());
        Assert.Equal(1, listed.GetProperty("items")[0].GetProperty("reportCount").GetInt64());
        using AmazonS3Client storage = GarageClients.Create();
        Assert.NotEmpty((await storage.ListObjectsV2Async(new() { BucketName = "symbols-sym" })).S3Objects ?? []);

        Account account = ApiTestHost.MakeAccount(AccountAccessLevel.Admin);
        string token = "avp_" + new string('A', 43);
        fixture.Api.PatIs(token, new PersonalAccessToken
        {
            Id = new PersonalAccessTokenId(5),
            AccountId = account.Id,
            Roles = AccountAccessLevel.Admin,
            Name = "vertical",
            TokenPrefix = token[..8],
            TokenHash = [],
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        });
        using var pat = new HttpRequestMessage(HttpMethod.Get, $"/admin/crashes/reports/{report.Id.Value}");
        pat.Headers.Authorization = new AuthenticationHeaderValue("Avalon", token);
        using HttpResponseMessage refusal = await fixture.Api.Client.SendAsync(pat);
        Assert.Equal(HttpStatusCode.Forbidden, refusal.StatusCode);
        Assert.True(refusal.Headers.CacheControl?.NoStore);
        await fixture.Database.AssertRestrictedRoleAsync();

        JsonElement fixedGroup = await fixture.PatchAsync(group, 0, "\"status\":\"fixed\",\"fixedInVersion\":\"0.4.0\",\"notes\":\"Engineering summary only\"");
        Assert.False(fixedGroup.GetProperty("isRegression").GetBoolean());
        JsonElement conflict = await fixture.PatchAsync(group, 0, "\"notes\":\"stale edit\"", HttpStatusCode.Conflict);
        Assert.Equal("CRASH_GROUP_CHANGED", conflict.GetProperty("code").GetString());
        Assert.Equal(1, conflict.GetProperty("currentRevision").GetInt64());
        // A fresh local cache and absent originals force the actual remote converted-cache path.
        await fixture.RequireRemoteCacheAsync();
        // Garage timestamps have second precision; this is a genuinely later upload than fixedAt.
        await Task.Delay(1100);
        StoredCrashObject regression = await fixture.UploadAsync();
        fixture.StartWorker();
        await fixture.WaitReportAsync(regression.Key, r => r.Status == CrashReportStatus.Processed);
        await fixture.StopWorkerAsync();
        JsonElement regressed = (await fixture.GetAsync($"/admin/crashes/groups/{group}")).GetProperty("group");
        Assert.True(regressed.GetProperty("isRegression").GetBoolean());
        Assert.Equal(1, regressed.GetProperty("regressionCount").GetInt64());
        Assert.Equal(2, regressed.GetProperty("reportCount").GetInt64());
        // Independent regressed state: ignored itself must clear the flag (T11-M1).
        JsonElement ignored = await fixture.PatchAsync(group, regressed.GetProperty("triageRevision").GetInt64(), "\"status\":\"ignored\"");
        Assert.False(ignored.GetProperty("isRegression").GetBoolean());
        Assert.Equal(1, ignored.GetProperty("regressionCount").GetInt64());
    }

    [IntegrationFact]
    public async Task Should_keep_one_contribution_after_restart_and_duplicate_listing()
    {
        await using var fixture = new CrashPipelineFixture();
        await fixture.InitializeAsync();
        StoredCrashObject source = await fixture.UploadAsync();
        fixture.StartWorker();
        await fixture.WaitReportAsync(source.Key, r => r.Status == CrashReportStatus.Processed);
        await fixture.StopWorkerAsync();
        var queue = new CrashQueue(fixture.Database.Factory);
        Assert.False(await queue.DiscoverAsync(source, DateTimeOffset.UtcNow, default));
        // Rewind only discovery scheduling to guarantee the restarted host lists the same object again.
        await VerticalDatabase.ExecuteAsync(fixture.Database.Owner, "UPDATE crash_discovery_versions SET last_visited_at = NULL, phase = 0, today_token = NULL, yesterday_token = NULL");
        fixture.StartWorker();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        while (true)
        {
            await using CrashesDbContext db = fixture.Database.Factory.CreateDbContext();
            if (await db.DiscoveryVersions.AnyAsync(d => d.LastVisitedAt != null, timeout.Token)) break;
            await Task.Delay(200, timeout.Token);
        }
        await fixture.StopWorkerAsync();
        await using CrashesDbContext final = fixture.Database.Factory.CreateDbContext();
        Assert.Single(await final.Reports.ToListAsync());
        Assert.Equal(1, (await final.Groups.SingleAsync()).ReportCount);
        Assert.Equal(1, (await final.Reports.SingleAsync()).AttemptCount);
    }

    [IntegrationFact]
    public async Task Should_expire_report_data_but_keep_group_history()
    {
        await using var fixture = new CrashPipelineFixture();
        await fixture.InitializeAsync();
        StoredCrashObject source = await fixture.UploadAsync();
        fixture.StartWorker();
        CrashReport report = await fixture.WaitReportAsync(source.Key, r => r.Status == CrashReportStatus.Processed);
        await fixture.StopWorkerAsync();
        await VerticalDatabase.ExecuteAsync(fixture.Database.Owner,
            "UPDATE crash_reports SET received_at = received_at - interval '91 days', expires_at = expires_at - interval '91 days'");
        using HttpResponseMessage expired = await fixture.Api.Client.GetAsync($"/admin/crashes/reports/{report.Id.Value}");
        Assert.Equal(HttpStatusCode.NotFound, expired.StatusCode);
        Assert.Contains("CRASH_REPORT_NOT_AVAILABLE", await expired.Content.ReadAsStringAsync());
        JsonElement detail = await fixture.GetAsync($"/admin/crashes/groups/{report.GroupId!.Value}");
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("latestStack").ValueKind);
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("latestReportId").ValueKind);
        Assert.Equal(0, detail.GetProperty("retainedReportCount").GetInt64());
        await fixture.CleanupAsync();
        await using CrashesDbContext db = fixture.Database.Factory.CreateDbContext();
        Assert.Empty(await db.Reports.ToListAsync());
        Assert.Equal(1, (await db.Groups.SingleAsync()).ReportCount);
        Assert.Equal(1, (await fixture.GetAsync("/admin/crashes/groups")).GetProperty("items")[0].GetProperty("reportCount").GetInt64());
        // Crash feature outage is isolated from identity's own HTTP surface.
        await VerticalDatabase.ExecuteAsync(fixture.Database.Owner, "DROP TABLE crash_reports CASCADE");
        using HttpResponseMessage unavailable = await fixture.Api.Client.GetAsync("/admin/crashes/groups");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        using HttpResponseMessage alive = await fixture.Api.Client.GetAsync("/alive");
        Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
    }

    [IntegrationFact]
    public async Task Should_export_nonempty_private_safe_telemetry_during_real_report_and_failures()
    {
        await using var fixture = new CrashPipelineFixture();
        await fixture.InitializeAsync();
        StoredCrashObject valid = await fixture.UploadAsync();
        fixture.StartWorker();
        CrashReport validReport = await fixture.WaitReportAsync(valid.Key, r => r.Status == CrashReportStatus.Processed);
        await fixture.StopWorkerAsync();
        StoredCrashObject tool = await fixture.UploadAsync(brokenThreads: true);
        fixture.StartWorker();
        CrashReport toolReport = await fixture.WaitReportAsync(tool.Key, r => r.Status == CrashReportStatus.RetryWait);
        Assert.Equal(CrashFailureCode.ToolFailed, toolReport.ReasonCode);
        await fixture.StopWorkerAsync();

        StoredCrashObject missing = await fixture.UploadAsync();
        var queue = new CrashQueue(fixture.Database.Factory);
        Assert.True(await queue.DiscoverAsync(missing, DateTimeOffset.UtcNow, default));
        using AmazonS3Client storage = GarageClients.Create();
        await storage.DeleteObjectAsync("crashes", missing.Key);
        fixture.StartWorker();
        CrashReport fetch = await fixture.WaitReportAsync(missing.Key, r => r.Status == CrashReportStatus.Failed);
        Assert.Equal(CrashFailureCode.ObjectMissing, fetch.ReasonCode);
        await fixture.StopWorkerAsync();

        StoredCrashObject database = await fixture.UploadAsync();
        // A real provider failure on completion, with a secret-bearing DB diagnostic. No fake native JSON.
        await VerticalDatabase.ExecuteAsync(fixture.Database.Owner, "CREATE FUNCTION fail_completion() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.status = 'processed' THEN RAISE EXCEPTION 'vertical-private-sql-secret'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_completion BEFORE UPDATE ON crash_reports FOR EACH ROW EXECUTE FUNCTION fail_completion()");
        fixture.StartWorker();
        CrashReport databaseReport = await fixture.WaitReportAsync(database.Key, r => r.Status == CrashReportStatus.Processing);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var health = new HttpClient();
        while (true)
        {
            using HttpResponseMessage response = await health.GetAsync("http://127.0.0.1:8080/health", timeout.Token);
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable) break;
            await Task.Delay(200, timeout.Token);
        }
        await fixture.StopWorkerAsync();
        await VerticalDatabase.ExecuteAsync(fixture.Database.Owner, "DROP TRIGGER fail_completion ON crash_reports; DROP FUNCTION fail_completion()");
        await using CrashesDbContext db = fixture.Database.Factory.CreateDbContext();
        CrashGroup group = await db.Groups.SingleAsync();
        (int spans, int logs, int metrics) = await TelemetryCapture.AssertAsync(fixture.ServiceName,
            [validReport.Id.Value, toolReport.Id.Value, fetch.Id.Value, databaseReport.Id.Value],
            [CrashPipelineFixture.LogCanary, CrashPipelineFixture.SqlCanary, valid.Key, tool.Key, missing.Key, database.Key,
                GarageClients.Required("WORKER_SECRET_KEY"), fixture.Database.Owner, group.Signature, group.CanonicalSignature, group.SignatureHash]);
        output.WriteLine($"Collector accepted {spans} report spans, {logs} finite report events, {metrics} worker metric names; four report IDs covered; private-value scan passed.");
    }
}
