using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;

namespace Avalon.World.Maintenance;

public interface IWorldEntryGate
{
    Task<WorldEntryDecision> CheckAsync(AccountId accountId, CancellationToken ct);
}

public readonly record struct WorldEntryDecision(bool Allowed, DateTime ValidUntilUtc)
{
    public bool IsValidAt(DateTime nowUtc) => Allowed && nowUtc < ValidUntilUtc;
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
