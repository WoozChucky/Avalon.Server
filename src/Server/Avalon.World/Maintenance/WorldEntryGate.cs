using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;

namespace Avalon.World.Maintenance;

public interface IWorldEntryGate
{
    Task<bool> CheckAsync(AccountId accountId, CancellationToken ct);
}

public sealed class WorldEntryGate(
    WorldId worldId,
    IWorldMaintenanceRepository maintenance,
    IAccountRepository accounts) : IWorldEntryGate
{
    public async Task<bool> CheckAsync(AccountId accountId, CancellationToken ct)
    {
        try
        {
            WorldMaintenanceState? state = await maintenance.ReadAsync(worldId, ct);
            if (state is null) return false;
            if (!state.Enabled) return true;

            Account? account = await accounts.FindByIdAsync(accountId, false, ct);
            return account is { Status: AccountStatus.Active }
                   && (account.AccessLevel & AccountAccessLevel.Admin) != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
