using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using Avalon.Api.Identity.Services;
using Avalon.Api.Testing;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.Login;
using Avalon.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.Authentication;

[Collection(LoginTelemetryCollection.Name)]
public sealed class LoginTelemetryShould
{
    private const string Source = "2001:db8:abcd:1234::/64";
    private static readonly string s_password = TestPasswords.Valid;
    private const string Hash = "private-mfa-hash";
    private const string Code = "123456";

    [Theory]
    [InlineData("password", "success", "success", 200)]
    [InlineData("password", "wrong", "invalid_credentials", 401)]
    [InlineData("password", "unknown", "invalid_credentials", 401)]
    [InlineData("password", "empty", "invalid_credentials", 401)]
    [InlineData("password", "locked", "locked", 429)]
    [InlineData("password", "last_failure", "locked", 429)]
    [InlineData("password", "source_limit", "rate_limited", 429)]
    [InlineData("password", "name_limit", "rate_limited", 429)]
    [InlineData("password", "mfa", "mfa_required", 200)]
    [InlineData("password", "banned", "banned", 403)]
    [InlineData("password", "deactivated", "deactivated", 403)]
    [InlineData("password", "write_refused", "invalid_credentials", 401)]
    [InlineData("mfa", "success", "success", 200)]
    [InlineData("mfa", "wrong", "mfa_failed", 401)]
    [InlineData("mfa", "unknown", "mfa_failed", 401)]
    [InlineData("mfa", "hash_gone", "mfa_failed", 401)]
    [InlineData("mfa", "hash_limit", "mfa_failed", 401)]
    [InlineData("mfa", "replayed", "mfa_failed", 401)]
    [InlineData("mfa", "hash_race", "mfa_failed", 401)]
    [InlineData("mfa", "locked", "locked", 429)]
    [InlineData("mfa", "last_failure", "locked", 429)]
    [InlineData("mfa", "source_limit", "rate_limited", 429)]
    [InlineData("mfa", "name_limit", "rate_limited", 429)]
    [InlineData("mfa", "banned", "banned", 403)]
    [InlineData("mfa", "deactivated", "deactivated", 403)]
    [InlineData("mfa", "write_refused", "mfa_failed", 401)]
    public async Task Record_one_bounded_metric_and_source_log_for_each_login_outcome(
        string method, string scenario, string result, int status)
    {
        var cache = new CounterCache();
        var logs = new LoginLogs();
        var measured = new ConcurrentQueue<(long Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, owner) =>
        {
            if (instrument.Meter.Name == "avalon-api" && instrument.Name == "avalon.api.logins")
                owner.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) => measured.Enqueue((value, tags.ToArray())));
        listener.Start();

        IMfaSetupRepository setups = Substitute.For<IMfaSetupRepository>();
        IPasswordVerifier verifier = Substitute.For<IPasswordVerifier>();
        verifier.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(scenario is not ("wrong" or "last_failure"));
        await using ApiTestHost host = await ApiTestHost.StartAsync(cache.Cache, services =>
        {
            services.AddScoped<IAccountService, AccountService>();
            services.AddSingleton(setups);
            services.AddSingleton(verifier);
            services.AddLogging(builder => builder.AddFilter<LoginLogs>((_, _) => true).AddProvider(logs));
        });
        Account account = ApiTestHost.MakeAccount(status: scenario switch
        {
            "banned" => AccountStatus.Banned,
            "deactivated" => AccountStatus.Deactivated,
            _ => AccountStatus.Active
        });
        account.Locked = scenario == "locked";
        account.LockedUntil = account.Locked ? DateTime.UtcNow.AddMinutes(10) : null;
        host.AccountNowIs(scenario == "unknown" ? null : account);
        host.AccountRepository.FindByUserNameAsync("CALLER", Arg.Any<CancellationToken>())
            .Returns(scenario == "unknown" ? null : account);
        host.Refresh.IssueAsync(Arg.Any<AccountId>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new RefreshIssueResult("private-refresh", DateTime.UtcNow.AddDays(1), Guid.NewGuid()));
        host.MfaHashes.GenerateHashAsync(Arg.Any<Account>()).Returns(Hash);
        host.Mfa.VerifyMFAAsync(Hash, Code, Arg.Any<CancellationToken>()).Returns(scenario switch
        {
            "wrong" or "last_failure" => new MFAVerifyResult(false, null),
            "replayed" => new MFAVerifyResult(false, null, MfaCodeRefusal.Replayed),
            "hash_race" => new MFAVerifyResult(false, null, MfaCodeRefusal.HashSpent),
            _ => new MFAVerifyResult(true, account.Id)
        });
        if (scenario == "mfa")
        {
            setups.FindByAccountIdAsync(account.Id, Arg.Any<CancellationToken>())
                .Returns(new MFASetup { Status = MfaSetupStatus.Confirmed });
        }
        if (scenario == "hash_gone")
            host.MfaHashes.GetAccountIdAsync(Hash).Returns((AccountId?)null);
        if (scenario == "hash_limit")
            host.MfaHashes.RecordAttemptAsync(account.Id).Returns(ApiTestHost.AuthConfig.MaxFailedMfaAttempts + 1L);
        if (scenario == "write_refused")
        {
            host.AccountRepository.TryRecordApiLoginAsync(account.Id, Arg.Any<string>(), Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>()).Returns(false);
        }
        if (scenario == "source_limit")
        {
            for (int i = 0; i < ApiTestHost.AuthConfig.MaxFailedLoginsPerSource; i++)
                await cache.Cache.IncrementAsync($"auth:source:{Source}:failedLogins", TimeSpan.FromMinutes(5));
        }
        if (scenario is "name_limit" or "last_failure")
        {
            int count = ApiTestHost.AuthConfig.MaxFailedLoginAttempts - (scenario == "last_failure" ? 1 : 0);
            for (int i = 0; i < count; i++)
                await cache.Cache.IncrementAsync(UsernameBudget.KeyFor("CALLER"), TimeSpan.FromMinutes(15));
        }

        logs.Entries.Clear();
        host.Client.DefaultRequestHeaders.Add(ApiTestHost.PeerHeader, "2001:db8:abcd:1234::99");
        using HttpResponseMessage response = method == "password"
            ? await host.Client.PostAsJsonAsync("/account/authenticate",
                new { username = "CALLER", password = scenario == "empty" ? "" : s_password })
            : await host.Client.PostAsJsonAsync("/mfa/verify", new { hash = Hash, code = Code });

        Assert.Equal(status, (int)response.StatusCode);
        (long value, KeyValuePair<string, object?>[] tags) = Assert.Single(measured);
        Assert.Equal(1, value);
        Assert.Equal(2, tags.Length);
        Assert.Equal(result, Assert.Single(tags, tag => tag.Key == "result").Value);
        Assert.Equal(method, Assert.Single(tags, tag => tag.Key == "method").Value);
        LogEntry outcome = Assert.Single(logs.Entries, entry => entry.Fields.ContainsKey("LoginResult"));
        Assert.Equal(result, outcome.Fields["LoginResult"]);
        Assert.Equal(method, outcome.Fields["LoginMethod"]);
        Assert.Equal(Source, outcome.Fields["ClientAddress"]);
        Assert.Equal(result == "rate_limited" ? LogLevel.Debug : LogLevel.Information, outcome.Level);
        foreach (object? field in outcome.Fields.Values)
        {
            string text = field?.ToString() ?? "";
            Assert.DoesNotContain(s_password, text, StringComparison.Ordinal);
            Assert.DoesNotContain(Hash, text, StringComparison.Ordinal);
            Assert.DoesNotContain(Code, text, StringComparison.Ordinal);
            Assert.DoesNotContain("CALLER", text, StringComparison.Ordinal);
        }
        if (result == "rate_limited")
        {
            Assert.DoesNotContain(logs.Entries, entry => entry.Level >= LogLevel.Warning &&
                entry.Category.StartsWith("Avalon.Infrastructure.Login.", StringComparison.Ordinal));
        }
    }

    private sealed record LogEntry(string Category, LogLevel Level, Dictionary<string, object?> Fields);

    private sealed class LoginLogs : ILoggerProvider
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Capture(categoryName, Entries);
        public void Dispose() { }

        private sealed class Capture(string category, ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (state is IEnumerable<KeyValuePair<string, object?>> fields)
                {
                    entries.Enqueue(new LogEntry(category, logLevel, fields.ToDictionary(p => p.Key, p => p.Value,
                        StringComparer.Ordinal)));
                }
            }
        }
    }
}

// The meter is process-wide; unrelated logins must not enter these exact-count assertions.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LoginTelemetryCollection
{
    public const string Name = "Login telemetry";
}
