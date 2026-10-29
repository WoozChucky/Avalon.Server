using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalon.Crashes.Queries;
using Avalon.Database.Crashes;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Avalon.Crashes.IntegrationTests.Queries;

[Trait("Category", "CrashesPostgres")]
public sealed class CrashReadStoreBacklogShould(ITestOutputHelper output) : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    private static readonly DateTimeOffset s_now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    public Task InitializeAsync() => _postgres.InitializeAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync();

    [IntegrationFact]
    public async Task Should_explain_real_page_and_group_queries_on_a_synthetic_backlog()
    {
        await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
        string stack = JsonSerializer.Serialize(new CrashAnalysis(1,
            new CrashIdentity("EXCEPTION_ACCESS_VIOLATION", 0xc0000005, null, 7, 42),
            new CrashSystem("Windows NT", "10", "26100", "x86_64", null),
            [new CrashThread(7, 42, [new CrashFrame("runtime.exe", null, "Fault", null, null, [], "context")])],
            [], new CrashMetadata(1, "runtime.exe", "2.0.0", "synthetic", 0xc0000005, "0x0000000000000000", 42, s_now),
            null, CrashQualityFlags.None, new ProcessorIdentity("0.27.0", "2.3.9", "v1")));
        // Fixture data only: migration-created schema, 1,000 groups and 100,000 reports over 100 UTC days.
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO crash_groups (id,signature_version,signature_hash,canonical_signature,signature,
              first_seen,last_seen,report_count,first_version,last_version,first_report_id,last_report_id,
              status,is_regression,regression_count,triage_revision)
            SELECT md5('group-' || i)::uuid,1,md5(i::text)||md5(i::text),'synthetic','runtime.exe!Fault',
              {s_now} - INTERVAL '99 days',{s_now},100,'1.0.0','4.0.0',md5('first-' || i)::uuid,md5('last-' || i)::uuid,
              CASE WHEN i % 4 = 0 THEN 'fixed' WHEN i % 4 = 1 THEN 'new' WHEN i % 4 = 2 THEN 'investigating' ELSE 'ignored' END,
              i % 29 = 0,0,0
            FROM generate_series(1,1000) AS i;
            INSERT INTO crash_reports (id,object_key,source_etag,source_size,received_at,expires_at,version,
              status,attempt_count,consecutive_tool_failures,group_id,processed_at,signature_version,stack,quality_flags,
              gpu_driver_module,gpu_driver_version,gpu_driver_source,os_build)
            SELECT md5('report-' || i)::uuid,'synthetic/' || i,'synthetic',1,
              {s_now} - ((i - 1) / 1000) * INTERVAL '1 day',
              {s_now} - ((i - 1) / 1000) * INTERVAL '1 day' + INTERVAL '90 days',
              (((i - 1) / 1000) % 4 + 1)::text || '.0.0','processed',1,0,
              md5('group-' || ((i - 1) % 1000 + 1))::uuid,{s_now},1,{stack}::jsonb,'none',
              CASE WHEN i % 2 = 0 THEN 'nvwgf2umx.dll' END,
              CASE WHEN i % 2 = 0 THEN '42' END,
              CASE WHEN i % 2 = 0 THEN 'faulting_module' ELSE 'unknown' END,
              CASE WHEN i % 3 = 0 THEN '26100' END
            FROM generate_series(1,100000) AS i;
            ANALYZE crash_groups;
            ANALYZE crash_reports;
            """);
        var probe = new QueryProbe();
        var read = new CrashReadStore(new ProbedContextFactory(db.Database.GetConnectionString()!, probe));
        CrashGroupPage page = await read.ListGroupsAsync(new(Version: "2.0.0", Since: s_now.AddDays(-20)), s_now, default);
        Assert.Equal(1000, page.Total);
        Assert.Equal(25, page.Items.Count);
        Assert.All(page.Items, x => Assert.Equal(90, x.RetainedReportCount));
        Assert.All(page.Items, x => Assert.Equal(5, x.MatchingReportCount));
        CapturedQuery[] pageQueries = probe.Queries.ToArray();
        Assert.Equal(5, pageQueries.Length);
        probe.Queries.Clear();
        CrashGroupDetail detail = (await read.GetGroupAsync(page.Items[0].Id, 2, 25, s_now, default))!;
        Assert.Equal(90, detail.RetainedReportCount);
        Assert.Equal(25, detail.Reports.Items.Count);
        CapturedQuery[] groupQueries = probe.Queries.ToArray();
        Assert.Equal(11, groupQueries.Length);   // the client's GPU breakdown (#1252) is the eleventh
        var text = new StringBuilder("# Task 10 actual PostgreSQL query plans\n\nSynthetic fixture: 1,000 groups; 100,000 processed reports over 100 days; 90,000 unexpired, 10,000 expired; four versions, mixed status/regression/driver/OS buckets. Migrated disposable database on localhost port 56449. No production data. UUID literals are sanitized. All queries are the actual intercepted read-store SQL with cloned parameter bindings. No planner options changed.\n\n");
        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync();
        foreach ((string name, CapturedQuery[] queries) in new[] { ("Filtered page", pageQueries), ("Group detail", groupQueries) })
        {
            for (int i = 0; i < queries.Length; i++)
            {
                CapturedQuery query = queries[i];
                text.Append("## ").Append(name).Append(" statement ").Append(i + 1).AppendLine();
                text.AppendLine("\n```sql").AppendLine(query.Sql).AppendLine("```\n\n```text");
                await using var command = new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS, FORMAT TEXT) " + query.Sql, connection);
                command.Parameters.AddRange(query.Parameters.Select(x => x.Clone()).ToArray());
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    text.AppendLine(Regex.Replace(reader.GetString(0), "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", "<synthetic-id>"));
                text.AppendLine("```\n");
            }
        }
        string plans = text.ToString();
        Assert.Contains("IX_crash_reports_group_id_received_at_id", plans);
        Assert.Contains("IX_crash_reports_version_received_at_group_id", plans);
        Assert.DoesNotContain("synthetic/", plans);
        string path = Environment.GetEnvironmentVariable("AVALON_CRASHES_QUERY_PLAN_OUTPUT")
            ?? Path.Combine(Path.GetTempPath(), "avalon-crash-query-plan-" + Guid.NewGuid().ToString("N") + ".md");
        await File.WriteAllTextAsync(path, plans);
        output.WriteLine("Sanitized actual EXPLAIN artifact: " + path);
    }
}
