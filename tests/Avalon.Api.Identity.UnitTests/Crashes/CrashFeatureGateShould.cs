using System.Net.Http.Headers;
using System.Net;
using System.Text;
using Avalon.Api.Contract;
using Avalon.Api.Identity.Crashes;
using Avalon.Api.Testing;
using Avalon.Database.Crashes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using NSubstitute;
using Xunit;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;

namespace Avalon.Api.Identity.UnitTests.Crashes;

public class CrashFeatureGateShould
{
    [Theory]
    [InlineData(false, "CRASH_ANALYSIS_DISABLED")]
    [InlineData(true, "CRASH_ANALYSIS_UNAVAILABLE")]
    public async Task Gate_before_binding_and_leave_login_routes_available(bool enabled, string code)
    {
        await using CrashApiFixture fixture = await CrashApiFixture.StartAsync(enabled, ready: false);
        using HttpResponseMessage response = await fixture.PatchAsync("{malformed");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains(code, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        fixture.Host.Accounts.Authenticate(Arg.Any<AuthenticateRequest>(), Arg.Any<IPAddress>(), Arg.Any<CancellationToken>())
            .Returns((new AuthenticateResponse { Token = "login-result" }, null, 0));
        using HttpResponseMessage login = await fixture.Host.Client.PostAsync("/account/authenticate", new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("login-result", await login.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        if (!enabled) await fixture.Readiness.DidNotReceive().IsReadyAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Keep_crash_connection_creation_lazy_and_fail_closed_without_configuration(bool enabled)
    {
        int created = 0;
        await using ApiTestHost host = await ApiTestHost.StartAsync([IdentityApi.Service], new ApiTestHostOptions
        {
            Settings = new Dictionary<string, string?> { ["Application:Crashes:Enabled"] = enabled.ToString() },
            Configure = services => services.AddSingleton<IDbContextFactory<CrashesDbContext>>(_ =>
            {
                created++;
                throw new InvalidOperationException("connection must not initialize while disabled");
            }),
        });
        Assert.Equal(0, created);
        Avalon.Domain.Auth.Account account = ApiTestHost.MakeAccount(AccountAccessLevel.Admin);
        host.AccountNowIs(account);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTestHost.Mint(account));
        using HttpResponseMessage response = await host.Client.GetAsync("/admin/crashes/groups?page=bad");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(enabled ? 1 : 0, created);
        using HttpResponseMessage second = await host.Client.GetAsync("/admin/crashes/groups");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        Assert.Equal(enabled ? 1 : 0, created);
    }
    [Fact]
    public async Task Bound_schema_probe_to_five_seconds_and_cache_failure_for_thirty_seconds()
    {
        var clock = new FakeTimeProvider(CrashApiFixture.Now);
        var factory = new SlowFactory();
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<CrashesDbContext>>(factory);
        await using ServiceProvider provider = services.BuildServiceProvider();
        using var readiness = new CrashSchemaReadiness(provider, clock);
        Task<bool> first = readiness.IsReadyAsync(CancellationToken.None);
        Assert.False(first.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.False(await first);
        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.False(await readiness.IsReadyAsync(CancellationToken.None));
        Assert.Equal(1, factory.Count);
        clock.Advance(TimeSpan.FromSeconds(1));
        Task<bool> retry = readiness.IsReadyAsync(CancellationToken.None);
        Assert.Equal(2, factory.Count);
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.False(await retry);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Preserve_client_cancellation_inside_a_provider_exception(bool clientCancelled)
    {
        using var cancellation = new CancellationTokenSource();
        if (clientCancelled) cancellation.Cancel();
        var failure = new InvalidOperationException("EF execution strategy",
            new NpgsqlException("Database read", new OperationCanceledException(cancellation.Token)));
        Assert.Equal(!clientCancelled, CrashProblemMapper.IsUnavailable(failure, cancellation.Token));
    }

    private sealed class SlowFactory : IDbContextFactory<CrashesDbContext>
    {
        public int Count { get; private set; }
        public CrashesDbContext CreateDbContext() => throw new InvalidOperationException();
        public async Task<CrashesDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            Count++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        }
    }
}
