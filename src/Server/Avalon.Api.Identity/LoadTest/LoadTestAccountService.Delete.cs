using System.Globalization;
using Avalon.Api.Contract;
using Avalon.Api.Hosting.Exceptions;
using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth;
using Avalon.Database.Character;
using Avalon.Domain.Auth;
using Avalon.Domain.Characters;
using Avalon.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;

namespace Avalon.Api.Identity.LoadTest;

/// <summary>
/// Deleting a run. An account is a load-test account to delete when it holds an Avalon license under a load-test
/// reference (of the run, when one is named) and nothing a run never gives: no other license, no purchase or refund,
/// no store identity, store creation or consolidation, no email, no role beyond <c>Player | PTR</c>. One that holds the
/// run's license and anything else is skipped and named in the reply, never deleted.
/// <para>
/// Every run is deleted only when asked for by name (<c>all</c>); a blank run id is refused, never read as every run.
/// Nothing is deleted while any selected account is online: in a game session (not ended, lease unexpired), or holding a
/// live gameplay fence (not blocked, lease unexpired) in any configured world; a character's <c>Online</c> flag counts
/// only beside one of those, so a flag a crashed world left behind never blocks. Nor while a configured world's
/// database is unavailable (409). Then, per world, the
/// accounts' characters (their rows cascade) and gameplay fences; then, in one auth transaction, the license
/// observations and holds that point at their licenses without cascading, the licenses, and the accounts (everything
/// else cascades); then the world sweep again, best-effort, for what a bot still running created meanwhile; then a
/// disconnect for each account deleted. A failure after the world sweep, or a bot entering a game during it (409), leaves
/// the auth rows, so the same request again, once the bots are stopped, finishes the run.
/// </para>
/// </summary>
public sealed partial class LoadTestAccountService
{
    /// <summary>All a run's account holds: anything beyond is a person's, and keeps the account.</summary>
    private const AccountAccessLevel BotAccess = AccountAccessLevel.Player | AccountAccessLevel.PTR;

    public async Task<LoadTestRunDeleted> DeleteAsync(AccountId admin, string? runId, bool all, CancellationToken ct)
    {
        if (all && runId is not null)
            throw new BusinessException("Name one run or every run, not both.");
        string? run = all ? null : ParseRunScope(runId);

        Selection selection = await auth.ExecuteAsync((db, token) => SelectAsync(db, run, token), ct);
        AccountId[] ids = selection.Accounts;
        AccountId[] deleted = [];
        List<string> skipped = [.. selection.Skipped];
        if (ids.Length > 0)
        {
            DateTime now = clock.GetUtcNow().UtcDateTime;
            WorldId[] worlds = AvailableWorlds();
            if (await auth.ExecuteAsync((db, token) => AnyInGameAsync(db, ids, now, token), ct))
                throw new LoadTestConflictException("A load-test account is in a game session; nothing was deleted.");
            foreach (WorldId world in worlds)
            {
                if (await AnyOnlineAsync(world, ids, now, ct))
                {
                    throw new LoadTestConflictException(
                        $"A load-test account is playing in world {world.Value}; nothing was deleted.");
                }
            }

            foreach (WorldId world in worlds)
                await DeleteCharactersAsync(world, ids, ct);

            deleted = await auth.ExecuteAsync((db, token) => DeleteAccountsAsync(db, run, ids, now, token), ct);

            // Kept by the second look inside the auth transaction: they gained something of a person's meanwhile.
            var gone = deleted.Select(id => id.Value).ToHashSet();
            skipped.AddRange(ids.Where(id => !gone.Contains(id.Value)).Select(id => selection.Names[id.Value]));
            skipped.Sort(StringComparer.Ordinal);

            if (deleted.Length > 0)
            {
                // A bot still running could have created a character between the sweep and the commit.
                foreach (WorldId world in worlds)
                    await SweepAgainAsync(world, deleted, ct);
                foreach (AccountId id in deleted)
                    await PublishDisconnectAsync(id);
            }
        }

        logger.LogInformation(
            "Admin {AdminId} deleted load-test run {RunId}: {Deleted} accounts deleted, {Skipped} skipped",
            admin.Value, run ?? "all", deleted.Length, skipped.Count);
        return new LoadTestRunDeleted(deleted.Length, skipped);
    }

    /// <summary>
    /// The run a request names: 3 ASCII letters, upper-cased. Absent, empty or blank is refused (400), so a request
    /// built from an unset variable never reaches every run; every run is asked for by name instead (<c>all=true</c>).
    /// </summary>
    public static string ParseRunScope(string? runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            throw new BusinessException("Name the run to delete (run=<id>), or ask for every run (all=true).");
        if (runId.Length != RunIdLength || !runId.All(char.IsAsciiLetter))
            throw new BusinessException($"A run id is {RunIdLength} ASCII letters.");
        return runId.ToUpperInvariant();
    }

    /// <summary>
    /// The accounts to delete with their usernames, and the usernames of those holding the run's license that are kept.
    /// </summary>
    private sealed record Selection(AccountId[] Accounts, IReadOnlyDictionary<long, string> Names, IReadOnlyList<string> Skipped);

    private static async Task<Selection> SelectAsync(AuthDbContext db, string? run, CancellationToken ct)
    {
        string prefix = (run is null ? ReferencePrefix : RunReferencePrefix(run)) + "%";
        AccountId[] matched = await db.GameLicenses
            .Where(l => l.Provider == StoreProviders.Avalon && EF.Functions.Like(l.LicenseReference, prefix))
            .Select(l => l.AccountId).Distinct().ToArrayAsync(ct);
        if (matched.Length == 0)
            return new Selection([], new Dictionary<long, string>(), []);

        var accounts = await db.Accounts.AsNoTracking().Where(a => matched.Contains(a.Id))
            .Select(a => new { a.Id, a.Username, a.AccessLevel, a.Email, a.IsStoreGenerated })
            .ToListAsync(ct);

        // Everything a run never gives an account, and an account holding any of it is a person's.
        var kept = new HashSet<long>(accounts
            .Where(a => (a.AccessLevel & ~BotAccess) != 0 || a.Email is not null || a.IsStoreGenerated)
            .Select(a => a.Id.Value));
        string anyLoadTest = ReferencePrefix + "%";
        Keep(kept, await db.GameLicenses
            .Where(l => matched.Contains(l.AccountId) &&
                        !(l.Provider == StoreProviders.Avalon && EF.Functions.Like(l.LicenseReference, anyLoadTest)))
            .Select(l => l.AccountId).ToListAsync(ct));
        Keep(kept, await db.ExternalIdentities.Where(e => matched.Contains(e.AccountId)).Select(e => e.AccountId)
            .ToListAsync(ct));
        Keep(kept, await db.PurchaseOrders.Where(o => matched.Contains(o.AccountId)).Select(o => o.AccountId)
            .ToListAsync(ct));
        Keep(kept, await db.PurchaseOrders.Where(o => matched.Contains(o.OriginalPurchaserAccountId))
            .Select(o => o.OriginalPurchaserAccountId).ToListAsync(ct));
        Keep(kept, await db.PaymentRefunds.Where(r => r.RequestedBy != null && matched.Contains(r.RequestedBy))
            .Select(r => r.RequestedBy!).ToListAsync(ct));
        // No foreign key to the account: deleting it would leave these behind.
        Keep(kept, await db.StoreAccountCreations.Where(c => matched.Contains(c.AccountId)).Select(c => c.AccountId)
            .ToListAsync(ct));
        Keep(kept, await db.AccountConsolidations.Where(c => matched.Contains(c.SourceAccountId))
            .Select(c => c.SourceAccountId).ToListAsync(ct));
        Keep(kept, await db.AccountConsolidations.Where(c => matched.Contains(c.TargetAccountId))
            .Select(c => c.TargetAccountId).ToListAsync(ct));

        return new Selection(
            accounts.Where(a => !kept.Contains(a.Id.Value)).Select(a => a.Id).ToArray(),
            accounts.Where(a => !kept.Contains(a.Id.Value)).ToDictionary(a => a.Id.Value, a => a.Username),
            accounts.Where(a => kept.Contains(a.Id.Value)).Select(a => a.Username).Order(StringComparer.Ordinal).ToList());
    }

    private static void Keep(HashSet<long> kept, List<AccountId> accounts)
    {
        foreach (AccountId account in accounts)
            kept.Add(account.Value);
    }

    /// <summary>
    /// Every configured world, each of which the sweep must reach: a world left out would keep the characters of
    /// accounts that no longer exist.
    /// </summary>
    private WorldId[] AvailableWorlds()
    {
        WorldId[] worlds = configuredWorlds.All.Select(w => w.Id).ToArray();
        foreach (WorldId world in worlds)
        {
            if (!configuredWorlds.IsAvailable(world))
            {
                throw new LoadTestConflictException(
                    $"World {world.Value}'s characters database is unavailable; nothing was deleted.");
            }
        }

        return worlds;
    }

    /// <summary>A game session not ended whose lease still runs, as admission counts one.</summary>
    private static Task<bool> AnyInGameAsync(AuthDbContext db, AccountId[] ids, DateTime now, CancellationToken ct) =>
        db.GameSessions.AnyAsync(s => ids.Contains(s.AccountId) && s.State != GameSessionState.Ended &&
                                      s.LeaseUntil > now, ct);

    /// <summary>
    /// A world still holding an account: its gameplay fence not blocked and its lease unexpired. A character's
    /// <c>Online</c> flag alone is not read (owner ruling): a world that crashed leaves it set, but its leases expire
    /// within a minute. A flag counts only beside a live game session (<see cref="AnyInGameAsync"/>) or a live fence
    /// in its world, and either of those refuses the delete by itself.
    /// </summary>
    private async Task<bool> AnyOnlineAsync(WorldId world, AccountId[] ids, DateTime now, CancellationToken ct)
    {
        await using CharacterDbContext db = worldContexts.CreateCharacters(world);
        return await db.AccountGameplayFences.AnyAsync(f => ids.Contains(f.AccountId) &&
                                                           f.Mode != GameplayFenceMode.Blocked && f.LeaseUntil > now, ct);
    }

    /// <summary>The accounts' characters, whose rows cascade, and their gameplay fences, in one transaction.</summary>
    private async Task DeleteCharactersAsync(WorldId world, AccountId[] ids, CancellationToken ct)
    {
        await using CharacterDbContext db = worldContexts.CreateCharacters(world);
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Characters.Where(c => ids.Contains(c.AccountId)).ExecuteDeleteAsync(ct);
        await db.AccountGameplayFences.Where(f => ids.Contains(f.AccountId)).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// After the auth commit, the sweep again for the accounts deleted. Best-effort: the accounts are gone already, so a
    /// failure is logged, not answered.
    /// </summary>
    private async Task SweepAgainAsync(WorldId world, AccountId[] ids, CancellationToken ct)
    {
        try
        {
            await DeleteCharactersAsync(world, ids, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not sweep world {WorldId} again after deleting {Count} load-test accounts",
                world.Value, ids.Length);
        }
    }

    /// <summary>
    /// The auth rows, in one transaction; returns the accounts deleted. The selection is made again inside it and only
    /// accounts still selected are deleted, so one that gained anything of a person's since is kept. One that entered a
    /// game since stops it with a 409: the run's characters are gone by then, its accounts are not.
    /// </summary>
    private static async Task<AccountId[]> DeleteAccountsAsync(AuthDbContext db, string? run, AccountId[] swept, DateTime now,
        CancellationToken ct)
    {
        Selection again = await SelectAsync(db, run, ct);
        var sweptIds = swept.Select(id => id.Value).ToHashSet();
        AccountId[] ids = again.Accounts.Where(id => sweptIds.Contains(id.Value)).ToArray();
        if (ids.Length == 0)
            return [];
        if (await AnyInGameAsync(db, ids, now, ct))
        {
            throw new LoadTestConflictException(
                "The run's characters were removed, but its accounts were not: a bot entered a game meanwhile. " +
                "Stop the bots and repeat the request.");
        }

        Guid[] licenses = await db.GameLicenses.Where(l => ids.Contains(l.AccountId)).Select(l => l.Id).ToArrayAsync(ct);
        // Both point at the licenses with a restricting foreign key.
        await db.LicenseObservations
            .Where(o => ids.Contains(o.AccountId) || (o.LicenseId != null && licenses.Contains(o.LicenseId.Value)))
            .ExecuteDeleteAsync(ct);
        await db.LicenseHolds.Where(h => licenses.Contains(h.LicenseId)).ExecuteDeleteAsync(ct);
        await db.GameLicenses.Where(l => ids.Contains(l.AccountId)).ExecuteDeleteAsync(ct);
        // Sessions, tokens, devices, MFA, email verifications and external identities cascade.
        await db.Accounts.Where(a => ids.Contains(a.Id)).ExecuteDeleteAsync(ct);
        return ids;
    }

    /// <summary>Kicks anything still holding the account; best-effort, as the delete is committed.</summary>
    private async Task PublishDisconnectAsync(AccountId account)
    {
        try
        {
            await cache.PublishAsync(CacheKeys.WorldAccountsDisconnectChannel,
                account.Value.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not publish a world disconnect for deleted load-test account {AccountId}",
                account.Value);
        }
    }
}
