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
/// Nothing is deleted while any selected account is in a game session, holds a live gameplay fence or has a character
/// online in any configured world, or while a configured world's database is unavailable (409). Then, per world, the
/// accounts' characters (their rows cascade) and gameplay fences; then, in one auth transaction, the license
/// observations and holds that point at their licenses without cascading, the licenses, and the accounts (everything
/// else cascades); then a disconnect for each. A failure after the world sweep leaves the auth rows, so the same request
/// again finishes the run.
/// </para>
/// </summary>
public sealed partial class LoadTestAccountService
{
    /// <summary>All a run's account holds: anything beyond is a person's, and keeps the account.</summary>
    private const AccountAccessLevel BotAccess = AccountAccessLevel.Player | AccountAccessLevel.PTR;

    public async Task<LoadTestRunDeleted> DeleteAsync(AccountId admin, string? runId, CancellationToken ct)
    {
        string? run = null;
        if (runId is not null)
        {
            if (runId.Length != RunIdLength || !runId.All(char.IsAsciiLetter))
                throw new BusinessException($"A run id is {RunIdLength} ASCII letters.");
            run = runId.ToUpperInvariant();
        }

        Selection selection = await auth.ExecuteAsync((db, token) => SelectAsync(db, run, token), ct);
        AccountId[] ids = selection.Accounts;
        int deleted = 0;
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

            foreach (AccountId id in ids)
                await PublishDisconnectAsync(id);
        }

        logger.LogInformation(
            "Admin {AdminId} deleted load-test run {RunId}: {Deleted} accounts deleted, {Skipped} skipped",
            admin.Value, run ?? "all", deleted, selection.Skipped.Count);
        return new LoadTestRunDeleted(deleted, selection.Skipped);
    }

    /// <summary>The accounts to delete, and the usernames of those holding the run's license that are kept.</summary>
    private sealed record Selection(AccountId[] Accounts, IReadOnlyList<string> Skipped);

    private static async Task<Selection> SelectAsync(AuthDbContext db, string? run, CancellationToken ct)
    {
        string prefix = (run is null ? ReferencePrefix : RunReferencePrefix(run)) + "%";
        AccountId[] matched = await db.GameLicenses
            .Where(l => l.Provider == StoreProviders.Avalon && EF.Functions.Like(l.LicenseReference, prefix))
            .Select(l => l.AccountId).Distinct().ToArrayAsync(ct);
        if (matched.Length == 0)
            return new Selection([], []);

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

    /// <summary>A character online, or a world still holding an account's gameplay fence.</summary>
    private async Task<bool> AnyOnlineAsync(WorldId world, AccountId[] ids, DateTime now, CancellationToken ct)
    {
        await using CharacterDbContext db = worldContexts.CreateCharacters(world);
        return await db.Characters.AnyAsync(c => ids.Contains(c.AccountId) && c.Online, ct) ||
               await db.AccountGameplayFences.AnyAsync(f => ids.Contains(f.AccountId) &&
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
    /// The auth rows, in one transaction. The selection is made again inside it and only accounts still selected are
    /// deleted, so one that gained anything of a person's since is kept; one that entered a game since stops the
    /// delete (its characters are already gone; it is a bot, and the request can be repeated once it has left).
    /// </summary>
    private static async Task<int> DeleteAccountsAsync(AuthDbContext db, string? run, AccountId[] swept, DateTime now,
        CancellationToken ct)
    {
        Selection again = await SelectAsync(db, run, ct);
        var sweptIds = swept.Select(id => id.Value).ToHashSet();
        AccountId[] ids = again.Accounts.Where(id => sweptIds.Contains(id.Value)).ToArray();
        if (ids.Length == 0)
            return 0;
        if (await AnyInGameAsync(db, ids, now, ct))
            throw new LoadTestConflictException("A load-test account entered a game session; its account was not deleted.");

        Guid[] licenses = await db.GameLicenses.Where(l => ids.Contains(l.AccountId)).Select(l => l.Id).ToArrayAsync(ct);
        // Both point at the licenses with a restricting foreign key.
        await db.LicenseObservations
            .Where(o => ids.Contains(o.AccountId) || (o.LicenseId != null && licenses.Contains(o.LicenseId.Value)))
            .ExecuteDeleteAsync(ct);
        await db.LicenseHolds.Where(h => licenses.Contains(h.LicenseId)).ExecuteDeleteAsync(ct);
        await db.GameLicenses.Where(l => ids.Contains(l.AccountId)).ExecuteDeleteAsync(ct);
        // Sessions, tokens, devices, MFA, email verifications and external identities cascade.
        return await db.Accounts.Where(a => ids.Contains(a.Id)).ExecuteDeleteAsync(ct);
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
