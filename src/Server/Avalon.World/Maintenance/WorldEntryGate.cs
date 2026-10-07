using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.World.Persistence;

namespace Avalon.World.Maintenance;

public interface IWorldEntryGate
{
    Task<WorldEntryDecision> CheckAsync(AccountId accountId, CancellationToken ct);
}

public readonly record struct WorldEntryDecision(bool Allowed, DateTime ValidUntilUtc)
{
    public bool IsValidAt(DateTime nowUtc) => Allowed && nowUtc < ValidUntilUtc;
}

public static class WorldEntryGateExtensions
{
    private static readonly TimeSpan s_checkTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Asks <paramref name="gate" /> on the thread pool, as character select and the final spawn do: a repository
    /// call must never begin on the simulation tick. A check that throws, or takes more than 5 s, is a refusal.
    /// </summary>
    public static Task<WorldEntryDecision> CheckOffTick(this IWorldEntryGate gate, AccountId accountId) =>
        WorldDatabaseWork.ThreadPool.Run(async () =>
        {
            try
            {
                return await gate.CheckAsync(accountId, CancellationToken.None)
                    .WaitAsync(s_checkTimeout, CancellationToken.None);
            }
            catch (Exception)
            {
                return default;
            }
        });
}

public sealed class WorldEntryGate(
    WorldId worldId,
    IWorldMaintenanceRepository maintenance,
    IAccountRepository accounts,
    TimeProvider? clock = null) : IWorldEntryGate
{
    public async Task<WorldEntryDecision> CheckAsync(AccountId accountId, CancellationToken ct)
    {
        try
        {
            WorldMaintenanceState? state = await maintenance.ReadAsync(worldId, ct);
            DateTime readAtUtc = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
            if (state is null) return default;

            Account? account = await accounts.FindByIdAsync(accountId, false, ct);
            if (account is not { Status: AccountStatus.Active }) return default;
            bool admin = (account.AccessLevel & AccountAccessLevel.Admin) != 0;
            if (state.IsCutoffActive((clock ?? TimeProvider.System).GetUtcNow().UtcDateTime) && !admin)
                return default;

            DateTime validUntilUtc = readAtUtc.AddSeconds(5);
            if (state.Enabled && !admin && state.DeadlineUtc is { } deadline && deadline < validUntilUtc)
                validUntilUtc = deadline;
            return new WorldEntryDecision(true, validUntilUtc);
        }
        catch (Exception)
        {
            return default;
        }
    }
}
