using System.Net;
using System.Text.Json;
using Avalon.Crashes.Queries;
using Avalon.Crashes.Triage;
using Avalon.Api.Contract.Crashes;
using Avalon.Crashes;
using Avalon.Database.Crashes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute.ExceptionExtensions;
using NSubstitute;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.Crashes;

public class CrashGroupsControllerShould
{
    [Fact]
    public async Task Expose_safe_list_and_group_shapes_with_primitive_ids_and_utc_times()
    {
        await using CrashApiFixture fixture = await CrashApiFixture.StartAsync();
        fixture.Reads.GetGroupAsync(Arg.Any<CrashGroupId>(), 1, 25, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new CrashGroupDetail(CrashApiFixture.Summary(), "engineering note", CrashApiFixture.Now,
                CrashApiFixture.Now.AddDays(-90), CrashApiFixture.Now, 1, CrashApiFixture.ReportId, CrashApiFixture.Now,
                new CrashThread(7, 4, [new CrashFrame("runtime.exe", "0x0000000000000020", "Fault", "engine/src/fault.cpp", 4, [], "context")]),
                CrashQualityFlags.ScanFrames, [new CrashVersionCount("1.0.1", 1)], [], [],
                new CrashReportPage([new CrashReportSummary(CrashApiFixture.ReportId, CrashApiFixture.Now,
                    CrashApiFixture.Now.AddDays(90), "1.0.1", 0xc0000005, CrashQualityFlags.None, "runtime.exe")], 1, 25, 1)));
        using HttpResponseMessage list = await fixture.Host.Client.GetAsync("/admin/crashes/groups");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.True(list.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        JsonElement row = json.RootElement.GetProperty("items")[0];
        Assert.Equal(CrashApiFixture.GroupId.Value, row.GetProperty("id").GetGuid());
        Assert.Equal("new", row.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("fixedInVersion").ValueKind);
        Assert.Equal(30, row.GetProperty("dailyCounts").GetArrayLength());
        Assert.Equal(500, row.GetProperty("reportCount").GetInt64());
        Assert.False(row.TryGetProperty("notes", out _));
        using HttpResponseMessage detail = await fixture.Host.Client.GetAsync($"/admin/crashes/groups/{CrashApiFixture.GroupId.Value}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        string body = await detail.Content.ReadAsStringAsync();
        using var group = JsonDocument.Parse(body);
        Assert.Equal(7, group.RootElement.GetProperty("latestStack").GetProperty("index").GetInt32());
        Assert.Equal("0x0000000000000020", group.RootElement.GetProperty("latestStack").GetProperty("frames")[0].GetProperty("moduleOffset").GetString());
        foreach (string forbidden in new[] { "logTail", "metadata", "sourceETag", "objectKey", "canonicalSignature", "dumpPath", "modules" })
        {
            Assert.DoesNotContain(forbidden, body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(forbidden, await list.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?page=%20")]
    [InlineData("?status=%20")]
    [InlineData("?version=%20")]
    [InlineData("?page=1001")]
    [InlineData("?pageSize=101")]
    [InlineData("?page=1&page=2")]
    [InlineData("?Status=new")]
    [InlineData("?status=NEW")]
    [InlineData("?status=0")]
    [InlineData("?version=../bad")]
    [InlineData("?since=tomorrow")]
    [InlineData("?since=2027-01-01T00:00:00Z")]
    [InlineData("?since=2026-10-20")]
    [InlineData("?since=2026-10-20T00:00:00%2B01:00")]
    [InlineData("?unknown=1")]
    public async Task Reject_invalid_or_repeated_list_queries(string query)
    {
        await using CrashApiFixture fixture = await CrashApiFixture.StartAsync();
        using HttpResponseMessage response = await fixture.Host.Client.GetAsync("/admin/crashes/groups" + query);
        await AssertInvalidAsync(response);
        await fixture.Reads.DidNotReceiveWithAnyArgs().ListGroupsAsync(default!, default, default);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"notes\":null}")]
    [InlineData("{\"triageRevision\":0}")]
    [InlineData("{\"triageRevision\":-1,\"notes\":null}")]
    [InlineData("{\"triageRevision\":1.5,\"notes\":null}")]
    [InlineData("{\"triageRevision\":0,\"notes\":null,\"notes\":\"x\"}")]
    [InlineData("{\"triageRevision\":0,\"reportCount\":1}")]
    [InlineData("{\"triageRevision\":0,\"status\":null}")]
    [InlineData("{\"triageRevision\":0,\"status\":\"FIXED\"}")]
    [InlineData("{\"triageRevision\":0,\"notes\":123}")]
    [InlineData("[")]
    public async Task Reject_malformed_patch_without_storage_access(string body)
    {
        await using CrashApiFixture fixture = await CrashApiFixture.StartAsync();
        using HttpResponseMessage response = await fixture.PatchAsync(body);
        await AssertInvalidAsync(response);
        await fixture.Triage.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task Preserve_presence_and_return_committed_snapshot_then_conflict_revision_only()
    {
        await using CrashApiFixture fixture = await CrashApiFixture.StartAsync();
        using HttpResponseMessage updated = await fixture.PatchAsync(JsonSerializer.Serialize(new UpdateCrashGroupRequest { TriageRevision = 7, Notes = null }));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var json = JsonDocument.Parse(await updated.Content.ReadAsStringAsync());
        Assert.Equal(8, json.RootElement.GetProperty("triageRevision").GetInt64());
        Assert.Equal(CrashApiFixture.GroupId.Value, json.RootElement.GetProperty("id").GetGuid());
        await fixture.Triage.Received().UpdateAsync(CrashApiFixture.GroupId,
            Arg.Is<CrashGroupPatch>(p => p.TriageRevision == 7 && p.Notes.IsSpecified && p.Notes.Value == null
                && !p.Status.IsSpecified && !p.IssueUrl.IsSpecified && !p.FixedInVersion.IsSpecified),
            CrashApiFixture.Now, Arg.Any<CancellationToken>());
        await fixture.Reads.DidNotReceiveWithAnyArgs().GetGroupAsync(default!, default, default, default, default);
        fixture.Triage.UpdateAsync(Arg.Any<CrashGroupId>(), Arg.Any<CrashGroupPatch>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(CrashTriageResult.Conflict(9));
        using HttpResponseMessage conflict = await fixture.PatchAsync("{\"triageRevision\":7,\"notes\":\"retry\"}");
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.True(conflict.Headers.CacheControl?.NoStore);
        string body = await conflict.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        Assert.Equal("CRASH_GROUP_CHANGED", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(9, problem.RootElement.GetProperty("currentRevision").GetInt64());
        Assert.DoesNotContain("notes", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bound_body_and_map_store_timeout_without_database_detail(bool wrapped)
    {
        await using CrashApiFixture fixture = await CrashApiFixture.StartAsync();
        using HttpResponseMessage large = await fixture.PatchAsync("{\"triageRevision\":0,\"notes\":\"" + new string('x', 16384) + "\"}");
        await AssertInvalidAsync(large);
        fixture.Reads.ListGroupsAsync(Arg.Any<CrashGroupFilter>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(wrapped ? new DbUpdateException("secret SQL", new TimeoutException("secret database hostname"))
                : new TimeoutException("secret database hostname"));
        using HttpResponseMessage timeout = await fixture.Host.Client.GetAsync("/admin/crashes/groups");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, timeout.StatusCode);
        Assert.True(timeout.Headers.CacheControl?.NoStore);
        string body = await timeout.Content.ReadAsStringAsync();
        Assert.Contains("CRASH_ANALYSIS_UNAVAILABLE", body, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reject_non_json_and_unknown_query_on_patch()
    {
        await using CrashApiFixture fixture = await CrashApiFixture.StartAsync();
        using var text = new StringContent("{\"triageRevision\":7,\"notes\":null}");
        using HttpResponseMessage contentType = await fixture.Host.Client.PatchAsync($"/admin/crashes/groups/{CrashApiFixture.GroupId.Value}", text);
        await AssertInvalidAsync(contentType);
        using HttpResponseMessage query = await fixture.PatchAsync("{\"triageRevision\":7,\"notes\":null}", "?unexpected=1");
        await AssertInvalidAsync(query);
    }

    [Theory]
    [InlineData("fixedInVersion", "1.2")]
    [InlineData("issueUrl", "https://github.com/owner/repo/issues/0")]
    [InlineData("issueUrl", "https://github.com:443/owner/repo/issues/1")]
    [InlineData("issueUrl", "https://github.com/owner/repo/issues/1?")]
    [InlineData("issueUrl", "https://github.com/owner/repo/issues/1#")]
    [InlineData("issueUrl", "https://github.com/owner/repo/Issues/1")]
    [InlineData("issueUrl", "https://github.com/owner/../issues/1")]
    [InlineData("issueUrl", "https://github.com/owner/repo/issues/%31")]
    [InlineData("issueUrl", "https://github.com/-owner/repo/issues/1")]
    public async Task Apply_real_triage_shape_validation_before_database_access(string field, string value)
    {
        IDbContextFactory<CrashesDbContext> factory = Substitute.For<IDbContextFactory<CrashesDbContext>>();
        await using CrashApiFixture fixture = await CrashApiFixture.StartAsync(configure: services =>
            services.AddSingleton<ICrashTriageStore>(new CrashTriageStore(factory)));
        using HttpResponseMessage response = await fixture.PatchAsync(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["triageRevision"] = 7,
            [field] = value,
        }));
        await AssertInvalidAsync(response);
        await factory.DidNotReceive().CreateDbContextAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(CrashTriageValidationCode.InvalidRevision)]
    [InlineData(CrashTriageValidationCode.EmptyPatch)]
    [InlineData(CrashTriageValidationCode.InvalidStatus)]
    [InlineData(CrashTriageValidationCode.InvalidFixedVersion)]
    [InlineData(CrashTriageValidationCode.InvalidIssueUrl)]
    [InlineData(CrashTriageValidationCode.NotesTooLong)]
    public async Task Map_finite_store_validation_and_missing_group(CrashTriageValidationCode code)
    {
        await using CrashApiFixture fixture = await CrashApiFixture.StartAsync();
        fixture.Triage.UpdateAsync(Arg.Any<CrashGroupId>(), Arg.Any<CrashGroupPatch>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(CrashTriageResult.Invalid(code));
        using HttpResponseMessage invalid = await fixture.PatchAsync("{\"triageRevision\":7,\"notes\":null}");
        await AssertInvalidAsync(invalid);
        fixture.Triage.UpdateAsync(Arg.Any<CrashGroupId>(), Arg.Any<CrashGroupPatch>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(CrashTriageResult.Missing());
        using HttpResponseMessage missing = await fixture.PatchAsync("{\"triageRevision\":7,\"notes\":null}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Contains("CRASH_GROUP_NOT_FOUND", await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(16384, HttpStatusCode.OK)]
    [InlineData(16385, HttpStatusCode.BadRequest)]
    public async Task Bound_unknown_length_body_by_bytes(int length, HttpStatusCode expected)
    {
        await using CrashApiFixture fixture = await CrashApiFixture.StartAsync();
        string json = "{\"triageRevision\":7,\"notes\":null}";
        using var body = new UnknownLengthContent(json.PadRight(length));
        using HttpResponseMessage response = await fixture.Host.Client.PatchAsync($"/admin/crashes/groups/{CrashApiFixture.GroupId.Value}", body);
        Assert.Equal(expected, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Return_finite_input_problem_for_unsupported_json_charset()
    {
        await using CrashApiFixture fixture = await CrashApiFixture.StartAsync();
        using var body = new StringContent("{\"triageRevision\":7,\"notes\":null}");
        body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") { CharSet = "not-an-encoding" };
        using HttpResponseMessage response = await fixture.Host.Client.PatchAsync($"/admin/crashes/groups/{CrashApiFixture.GroupId.Value}", body);
        await AssertInvalidAsync(response);
    }

    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly string _text;

        public UnknownLengthContent(string text)
        {
            _text = text;
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(_text)).AsTask();
    }

    internal static async Task AssertInvalidAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("CRASH_INVALID_INPUT", json.RootElement.GetProperty("code").GetString());
    }
}
