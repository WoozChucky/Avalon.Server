using System.Net.Http.Headers;
using System.Text;
using Avalon.Api.Identity.Crashes;
using Avalon.Api.Testing;
using Avalon.Common.Accounts;
using Avalon.Crashes.Queries;
using Avalon.Crashes.Triage;
using Avalon.Crashes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Avalon.Api.Identity.UnitTests.Crashes;

internal sealed class CrashApiFixture : IAsyncDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    public static readonly CrashGroupId GroupId = new(Guid.Parse("624c135d-697e-4916-a0a4-a8d03dc830d2"));
    public static readonly CrashReportId ReportId = new(Guid.Parse("34872e59-4444-494d-b2b9-9387f65b01ea"));
    public ICrashReadStore Reads { get; } = Substitute.For<ICrashReadStore>();
    public ICrashTriageStore Triage { get; } = Substitute.For<ICrashTriageStore>();
    public ICrashSchemaReadiness Readiness { get; } = Substitute.For<ICrashSchemaReadiness>();
    public FakeTimeProvider Clock { get; } = new(Now);
    public ApiTestHost Host { get; private set; } = null!;

    public static async Task<CrashApiFixture> StartAsync(bool enabled = true, bool ready = true, Action<IServiceCollection>? configure = null)
    {
        var fixture = new CrashApiFixture();
        fixture.Readiness.IsReadyAsync(Arg.Any<CancellationToken>()).Returns(ready);
        fixture.Reads.ListGroupsAsync(Arg.Any<CrashGroupFilter>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new CrashGroupPage([Summary()], 1, 25, 1, Now, 90, Now.AddDays(-90)));
        fixture.Triage.UpdateAsync(Arg.Any<CrashGroupId>(), Arg.Any<CrashGroupPatch>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(CrashTriageResult.Updated(new CrashGroupTriage(GroupId, CrashGroupStatus.Investigating, null, null, null,
                false, 1, null, 8, Now, null)));
        fixture.Host = await ApiTestHost.StartAsync([IdentityApi.Service], new ApiTestHostOptions
        {
            Settings = new Dictionary<string, string?> { ["Application:Crashes:Enabled"] = enabled.ToString() },
            Configure = services =>
            {
                services.AddSingleton(fixture.Reads);
                services.AddSingleton(fixture.Triage);
                services.AddSingleton(fixture.Readiness);
                services.AddSingleton<TimeProvider>(fixture.Clock);
                configure?.Invoke(services);
            },
        });
        Avalon.Domain.Auth.Account account = ApiTestHost.MakeAccount(AccountAccessLevel.Admin);
        fixture.Host.AccountNowIs(account);
        fixture.Host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTestHost.Mint(account));
        return fixture;
    }

    public static CrashGroupSummary Summary() => new(GroupId, "runtime!Fault", 1, CrashGroupStatus.New,
        null, null, false, 1, null, 7, null, null, Now.AddDays(-1), Now, "1.0.0", "1.0.1", 500, 1, 1,
        Enumerable.Range(0, 30).Select(i => new CrashDailyCount(DateOnly.FromDateTime(Now.UtcDateTime).AddDays(i - 29), i == 29 ? 1 : 0)).ToArray());

    public Task<HttpResponseMessage> PatchAsync(string json, string? query = null) => Host.Client.PatchAsync(
        $"/admin/crashes/groups/{GroupId.Value}{query}", new StringContent(json, Encoding.UTF8, "application/json"));

    public ValueTask DisposeAsync() => Host.DisposeAsync();
}
