using System.Net;
using System.Net.Http.Json;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.Services;
using Avalon.Server.Auth.UnitTests.Services;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;
using static Avalon.Api.UnitTests.Authentication.ApiAuthHost;

namespace Avalon.Api.UnitTests.Authentication;

/// <summary>
/// #478: <c>/mfa/verify</c> is anonymous, and it counted nothing: a wrong code was not counted
/// against its hash, its source or its account, and the account lock was not checked, so the codes
/// of an account whose password was known could be guessed without limit for as long as the hash
/// lived. It now runs the game client's MFA policy. Runs the real controller over HTTP.
/// </summary>
public sealed class RestMfaVerifyShould : IAsyncLifetime
{
    private const string Hash = "hash";
    private const string RightCode = "123456";

    private readonly CounterCache _counters = new();
    private ApiAuthHost _host = null!;

    public async Task InitializeAsync() => _host = await ApiAuthHost.StartAsync(_counters.Cache);

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private Account AccountIs(bool locked = false)
    {
        Account account = MakeAccount();
        if (locked)
        {
            account.Locked = true;
            account.LockedUntil = DateTime.UtcNow.AddMinutes(10);
        }

        _host.AccountRepository.FindByIdAsync(Arg.Is<AccountId>(id => id.Value == AccountIdValue), Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(account);
        _host.Mfa.VerifyMFAAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MFAVerifyResult(false, null));
        _host.Mfa.VerifyMFAAsync(Arg.Any<string>(), RightCode, Arg.Any<CancellationToken>())
            .Returns(new MFAVerifyResult(true, new AccountId(AccountIdValue)));
        _host.Refresh.IssueAsync(Arg.Any<AccountId>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new RefreshIssueResult("refresh-new", DateTime.UtcNow.AddDays(30), Guid.NewGuid()));
        return account;
    }

    private Task<HttpResponseMessage> VerifyAsync(string code) =>
        _host.Client.PostAsJsonAsync("/mfa/verify", new { hash = Hash, code });

    private int CodesChecked() => _host.Mfa.ReceivedCalls()
        .Count(c => c.GetMethodInfo().Name == nameof(IMFAService.VerifyMFAAsync));

    private static async Task AssertLocked(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("LOCKED", problem?.Detail);
    }

    [Fact]
    public async Task Refuse_a_code_on_a_hash_past_its_attempt_limit_and_delete_the_hash()
    {
        AccountIs();
        _host.MfaHashes.RecordAttemptAsync(Arg.Any<AccountId>()).Returns(AuthConfig.MaxFailedMfaAttempts + 1L);

        using HttpResponseMessage response = await VerifyAsync(RightCode);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, CodesChecked());
        await _host.MfaHashes.Received(1).CleanupHash(Hash);
    }

    [Fact]
    public async Task Delete_the_hash_on_its_last_wrong_code()
    {
        AccountIs();
        _host.MfaHashes.RecordAttemptAsync(Arg.Any<AccountId>()).Returns((long)AuthConfig.MaxFailedMfaAttempts);

        using HttpResponseMessage response = await VerifyAsync("000000");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await _host.MfaHashes.Received(1).CleanupHash(Hash);
    }

    /// <summary>
    /// A fresh password login makes a fresh hash, so the per-hash cap alone never ends a guessing
    /// run: each code also spends the account's username budget, and its last slot locks the row.
    /// </summary>
    [Fact]
    public async Task Lock_the_account_once_wrong_codes_spend_its_username_budget()
    {
        AccountIs();

        for (var i = 1; i < AuthConfig.MaxFailedLoginAttempts; i++)
        {
            using HttpResponseMessage wrong = await VerifyAsync("000000");
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        using (HttpResponseMessage last = await VerifyAsync("000000"))
            await AssertLocked(last);
        await _host.AccountRepository.Received(1).RecordFailedLoginAsync(Arg.Is<AccountId>(id => id.Value == AccountIdValue),
            Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Is<DateTime?>(until => until != null), Arg.Any<CancellationToken>());

        // The right code is now refused without being checked.
        using (HttpResponseMessage right = await VerifyAsync(RightCode))
            await AssertLocked(right);
        Assert.Equal(AuthConfig.MaxFailedLoginAttempts, CodesChecked());
        await _host.Refresh.DidNotReceiveWithAnyArgs().IssueAsync(default!, default, default);
    }

    /// <summary>#478 review: a replayed code, or one that lost the hash to another verify, is refused but not counted.</summary>
    [Theory]
    [InlineData(MfaCodeRefusal.Replayed)]
    [InlineData(MfaCodeRefusal.HashSpent)]
    public async Task Refuse_a_replayed_or_raced_code_without_counting_a_failed_login(MfaCodeRefusal refusal)
    {
        AccountIs();
        _host.Mfa.VerifyMFAAsync(Arg.Any<string>(), RightCode, Arg.Any<CancellationToken>())
            .Returns(new MFAVerifyResult(false, null, refusal));
        _host.MfaHashes.RecordAttemptAsync(Arg.Any<AccountId>()).Returns((long)AuthConfig.MaxFailedMfaAttempts);

        using HttpResponseMessage response = await VerifyAsync(RightCode);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await _host.AccountRepository.DidNotReceiveWithAnyArgs().RecordFailedLoginAsync(default!, default!, default, default, default);
        await _host.MfaHashes.DidNotReceiveWithAnyArgs().CleanupHash(default!);
    }

    [Fact]
    public async Task Refuse_a_source_past_its_budget_before_checking_the_code()
    {
        AccountIs();
        using (await VerifyAsync("000000")) { }
        string sourceKey = Assert.Single(_counters.KeysStartingWith("auth:source:"));
        while (_counters.CountOf(sourceKey) < AuthConfig.MaxFailedLoginsPerSource)
            await _counters.Cache.IncrementAsync(sourceKey, TimeSpan.FromMinutes(15));

        using HttpResponseMessage response = await VerifyAsync(RightCode);

        await AssertLocked(response);
        Assert.Equal(1, CodesChecked());
    }

    [Fact]
    public async Task Refuse_a_locked_account_before_checking_the_code()
    {
        AccountIs(locked: true);

        using HttpResponseMessage response = await VerifyAsync(RightCode);

        await AssertLocked(response);
        Assert.Equal(0, CodesChecked());
    }

    /// <summary>
    /// The login is written by column, and refused while the account is locked (#484): a lock that
    /// landed after the row was read survives, and the right code gets a wrong code's answer.
    /// </summary>
    [Fact]
    public async Task Refuse_the_session_when_a_lock_landed_before_the_login_was_written()
    {
        AccountIs();
        _host.AccountRepository.TryRecordApiLoginAsync(Arg.Any<AccountId>(), Arg.Any<string>(), Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>()).Returns(false);

        using HttpResponseMessage response = await VerifyAsync(RightCode);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await _host.Refresh.DidNotReceiveWithAnyArgs().IssueAsync(default!, default, default);
        await _host.AccountRepository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task Clear_the_username_count_once_the_code_completes_the_login()
    {
        AccountIs();
        using (await VerifyAsync("000000")) { }
        Assert.Single(_counters.UsernameKeys);

        using HttpResponseMessage response = await VerifyAsync(RightCode);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_counters.UsernameKeys);
        await _host.AccountRepository.Received(1).TryRecordApiLoginAsync(Arg.Is<AccountId>(id => id.Value == AccountIdValue),
            Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _host.AccountRepository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }
}
