using System.Text;
using Avalon.Api.Exceptions;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Api.UnitTests.Services;

/// <summary>
/// #495: a rotation read the token, wrote it back revoked, then inserted its child, so two
/// rotations of one token that both read it live both got a child. The revoke is now a conditional
/// write, and only the rotation whose write revoked the parent goes on. The other gets 401, and, as
/// a second tab or a client retry does, it does not revoke the family when the parent was rotated
/// under five seconds ago and its child is still unused; a replay outside that is still a reuse.
/// Real repository, real schema.
/// </summary>
public sealed class RefreshRotationRaceShould : IDisposable
{
    private readonly SqliteAuthDatabase _database = new();

    public void Dispose() => _database.Dispose();

    /// <summary>One browser: the same source and User-Agent for both of its tabs.</summary>
    private static readonly RefreshCaller Tab = RefreshCaller.From(System.Net.IPAddress.Parse("203.0.113.7"), "Browser/1.0");

    private async Task<Account> AccountAsync()
    {
        await using AuthDbContext context = _database.CreateDbContext();
        var account = new Account
        {
            Username = "ROTATOR",
            Email = "rotator@avalon.monster",
            Salt = [1],
            Verifier = Encoding.UTF8.GetBytes("unused"),
            JoinDate = DateTime.UtcNow,
            LastLogin = DateTime.UtcNow,
        };
        context.Accounts.Add(account);
        await context.SaveChangesAsync();
        return account;
    }

    /// <summary>
    /// The real repository, except that every rotation's read waits until both rotations have read
    /// the token, so both see it live. The one SQLite connection is not thread-safe, so the
    /// statements themselves take turns.
    /// </summary>
    private sealed class BothReadFirst(IRefreshTokenRepository inner) : IRefreshTokenRepository
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly TaskCompletionSource _bothRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reads;

        private async Task<T> OneAtATime<T>(Func<Task<T>> statement)
        {
            await _gate.WaitAsync();
            try { return await statement(); }
            finally { _gate.Release(); }
        }

        public async Task<RefreshToken?> FindByHashAsync(byte[] hash, CancellationToken cancellationToken = default)
        {
            RefreshToken? row = await OneAtATime(() => inner.FindByHashAsync(hash, cancellationToken));
            if (Interlocked.Increment(ref _reads) == 2) _bothRead.SetResult();
            await _bothRead.Task;
            return row;
        }

        public Task<RefreshRotation> RotateAsync(RefreshToken parent, RefreshToken child, DateTime now,
            CancellationToken cancellationToken = default) =>
            OneAtATime(() => inner.RotateAsync(parent, child, now, cancellationToken));

        public Task<RefreshToken?> FindChildAsync(Guid familyId, uint index,
            CancellationToken cancellationToken = default) =>
            OneAtATime(() => inner.FindChildAsync(familyId, index, cancellationToken));

        public Task<bool> CreateIfCredentialsCurrentAsync(RefreshToken token,
            CancellationToken cancellationToken = default) =>
            OneAtATime(() => inner.CreateIfCredentialsCurrentAsync(token, cancellationToken));

        public Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken cancellationToken = default) =>
            OneAtATime(() => inner.CreateAsync(token, cancellationToken));

        public async Task UpdateAsync(RefreshToken token, CancellationToken cancellationToken = default) =>
            await OneAtATime(async () => { await inner.UpdateAsync(token, cancellationToken); return 0; });

        public Task<int> RevokeFamilyAsync(Guid familyId, CancellationToken cancellationToken = default) =>
            OneAtATime(() => inner.RevokeFamilyAsync(familyId, cancellationToken));

        public Task<int> RevokeAllForAccountAsync(AccountId accountId, CancellationToken cancellationToken = default) =>
            OneAtATime(() => inner.RevokeAllForAccountAsync(accountId, cancellationToken));
    }

    [Fact]
    public async Task Let_exactly_one_of_two_concurrent_rotations_of_one_token_succeed()
    {
        Account account = await AccountAsync();
        RefreshTokenRepository real = new(_database);
        RefreshIssueResult issued = await new RefreshTokenService(real, new SecureRandom(), TimeProvider.System)
            .IssueAsync(account.Id, 0);
        var service = new RefreshTokenService(new BothReadFirst(real), new SecureRandom(), TimeProvider.System);

        Task<RefreshRotateResult>[] rotations =
            [service.RotateAsync(issued.RawToken, Tab), service.RotateAsync(issued.RawToken, Tab)];
        try { await Task.WhenAll(rotations); } catch { /* inspected below */ }

        Assert.Single(rotations, r => r.IsCompletedSuccessfully);
        Task loser = Assert.Single(rotations, r => r.IsFaulted);
        // Inside the grace window: a plain 401, not a reuse, so the winner's session survives.
        Assert.IsType<RefreshAlreadyRotatedException>(loser.Exception!.InnerException);

        // One child, not two: the loser inserted nothing, and the winner's child is still live.
        await using AuthDbContext context = _database.CreateDbContext();
        Assert.Equal(2, await context.RefreshTokens.CountAsync(t => t.FamilyId == issued.FamilyId));
        Assert.Equal(1, await context.RefreshTokens.CountAsync(t => t.FamilyId == issued.FamilyId && !t.Revoked));
    }

    /// <summary>A clock the test moves.</summary>
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private async Task<(RefreshTokenService Service, Clock Clock, RefreshIssueResult Issued)> RotatedOnceAsync()
    {
        Account account = await AccountAsync();
        var clock = new Clock(DateTimeOffset.UtcNow);
        var service = new RefreshTokenService(new RefreshTokenRepository(_database), new SecureRandom(), clock);
        RefreshIssueResult issued = await service.IssueAsync(account.Id, 0);
        await service.RotateAsync(issued.RawToken, Tab);
        return (service, clock, issued);
    }

    private async Task<int> LiveInFamilyAsync(Guid familyId)
    {
        await using AuthDbContext context = _database.CreateDbContext();
        return await context.RefreshTokens.CountAsync(t => t.FamilyId == familyId && !t.Revoked);
    }

    [Fact]
    public async Task Answer_a_resent_token_inside_the_grace_window_without_revoking_the_family()
    {
        var (service, clock, issued) = await RotatedOnceAsync();
        clock.Now += TimeSpan.FromSeconds(4);

        Exception refused = await Assert.ThrowsAnyAsync<Exception>(() => service.RotateAsync(issued.RawToken, Tab));

        Assert.IsType<RefreshAlreadyRotatedException>(refused);
        Assert.Equal(1, await LiveInFamilyAsync(issued.FamilyId));
    }

    /// <summary>#495 review: someone else replaying inside the window is not the second tab.</summary>
    [Fact]
    public async Task Treat_a_replay_from_another_source_inside_the_grace_window_as_a_reuse()
    {
        var (service, clock, issued) = await RotatedOnceAsync();
        clock.Now += TimeSpan.FromSeconds(1);

        await Assert.ThrowsAsync<RefreshTheftException>(() => service.RotateAsync(issued.RawToken,
            RefreshCaller.From(System.Net.IPAddress.Parse("198.51.100.9"), "Browser/1.0")));

        Assert.Equal(0, await LiveInFamilyAsync(issued.FamilyId));
    }

    [Fact]
    public async Task Treat_a_replay_with_another_user_agent_inside_the_grace_window_as_a_reuse()
    {
        var (service, clock, issued) = await RotatedOnceAsync();
        clock.Now += TimeSpan.FromSeconds(1);

        await Assert.ThrowsAsync<RefreshTheftException>(() => service.RotateAsync(issued.RawToken,
            RefreshCaller.From(System.Net.IPAddress.Parse("203.0.113.7"), "curl/8.0")));

        Assert.Equal(0, await LiveInFamilyAsync(issued.FamilyId));
    }

    [Fact]
    public async Task Forgive_the_same_ipv6_64_with_the_same_user_agent()
    {
        Account account = await AccountAsync();
        var clock = new Clock(DateTimeOffset.UtcNow);
        var service = new RefreshTokenService(new RefreshTokenRepository(_database), new SecureRandom(), clock);
        RefreshIssueResult issued = await service.IssueAsync(account.Id, 0);
        await service.RotateAsync(issued.RawToken,
            RefreshCaller.From(System.Net.IPAddress.Parse("2001:db8:1:2::10"), "Browser/1.0"));
        clock.Now += TimeSpan.FromSeconds(1);

        await Assert.ThrowsAsync<RefreshAlreadyRotatedException>(() => service.RotateAsync(issued.RawToken,
            RefreshCaller.From(System.Net.IPAddress.Parse("2001:db8:1:2::99"), "Browser/1.0")));

        Assert.Equal(1, await LiveInFamilyAsync(issued.FamilyId));
    }

    [Fact]
    public async Task Treat_a_replay_after_the_grace_window_as_a_reuse()
    {
        var (service, clock, issued) = await RotatedOnceAsync();
        clock.Now += TimeSpan.FromSeconds(6);

        await Assert.ThrowsAsync<RefreshTheftException>(() => service.RotateAsync(issued.RawToken, Tab));

        Assert.Equal(0, await LiveInFamilyAsync(issued.FamilyId));
    }

    [Fact]
    public async Task Treat_a_replay_as_a_reuse_once_the_child_has_been_used()
    {
        var (service, clock, issued) = await RotatedOnceAsync();
        await using (AuthDbContext context = _database.CreateDbContext())
            await context.RefreshTokens.Where(t => t.FamilyId == issued.FamilyId && t.Index == 1)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Usages, 1u).SetProperty(t => t.Revoked, true));
        clock.Now += TimeSpan.FromSeconds(1);

        await Assert.ThrowsAsync<RefreshTheftException>(() => service.RotateAsync(issued.RawToken, Tab));
    }

    [Fact]
    public async Task Answer_a_rotation_of_a_token_revoked_after_it_was_read_as_a_reuse()
    {
        Account account = await AccountAsync();
        RefreshTokenRepository real = new(_database);
        var service = new RefreshTokenService(real, new SecureRandom(), TimeProvider.System);
        RefreshIssueResult issued = await service.IssueAsync(account.Id, 0);
        RefreshToken parent = (await real.FindByHashAsync(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(issued.RawToken))))!;
        // Revoked between the rotation's read (parent, live) and its write.
        await real.RevokeAllForAccountAsync(account.Id);

        RefreshRotation outcome = await real.RotateAsync(parent, new RefreshToken
        {
            AccountId = account.Id, FamilyId = parent.FamilyId, Index = 1, Hash = [7, 7, 7],
            CreatedAt = DateTime.UtcNow, ExpiresAt = parent.ExpiresAt,
        }, DateTime.UtcNow);

        Assert.Equal(RefreshRotation.ParentNotLive, outcome);
        await using AuthDbContext context = _database.CreateDbContext();
        Assert.Equal(1, await context.RefreshTokens.CountAsync(t => t.FamilyId == issued.FamilyId));
        Assert.True(await context.RefreshTokens.AllAsync(t => t.Revoked));
    }
}
