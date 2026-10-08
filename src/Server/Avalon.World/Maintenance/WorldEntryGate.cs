using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.World.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    private static readonly EntryCheckFailureLog s_failures = new(TimeProvider.System);

    /// <summary>
    /// Asks <paramref name="gate" /> on <paramref name="work" /> (the shared database work queue when null), as
    /// character select and the final spawn do: a repository call must never begin on the simulation tick. The task
    /// never faults, so the continuation that waits on it always runs: a check the work queue refuses (saturated),
    /// one that throws and one that takes more than 5 s are each a refusal, logged by exception type at most once per
    /// <see cref="EntryCheckFailureLog.Interval" />.
    /// </summary>
    public static Task<WorldEntryDecision> CheckOffTick(this IWorldEntryGate gate, AccountId accountId, ILogger logger,
        IWorldDatabaseWork? work = null)
    {
        Task<WorldEntryDecision> check;
        try
        {
            check = (work ?? WorldDatabaseWork.ThreadPool).Run(() =>
                gate.CheckAsync(accountId, CancellationToken.None).WaitAsync(s_checkTimeout, CancellationToken.None));
        }
        catch (Exception e)
        {
            check = Task.FromException<WorldEntryDecision>(e);
        }

        return check.IsCompletedSuccessfully ? check : RefusedOnFailure(check, logger);
    }

    private static async Task<WorldEntryDecision> RefusedOnFailure(Task<WorldEntryDecision> check, ILogger logger)
    {
        try
        {
            return await check.ConfigureAwait(false);
        }
        catch (Exception e)
        {
            s_failures.Report(logger, "The world entry check", e);
            return default;
        }
    }
}

/// <summary>
/// Logs a failed world entry check at Error, by exception type only (a database message can carry a host name), at most
/// once per <see cref="Interval" />, with how many failures were left out since, so an outage that refuses every entry
/// cannot flood the log. Thread-safe: the checks fail on the thread pool.
/// </summary>
internal sealed class EntryCheckFailureLog(TimeProvider time)
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private long _lastLoggedTicks = long.MinValue;
    private int _suppressed;

    public void Report(ILogger logger, string what, Exception e)
    {
        long now = time.GetUtcNow().UtcTicks;
        long last = Interlocked.Read(ref _lastLoggedTicks);
        if ((last != long.MinValue && now - last < Interval.Ticks) ||
            Interlocked.CompareExchange(ref _lastLoggedTicks, now, last) != last)
        {
            Interlocked.Increment(ref _suppressed);
            return;
        }

        logger.LogError("{What} failed with {ExceptionType}; entry was refused. {Suppressed} earlier failures were not logged",
            what, e.GetType().Name, Interlocked.Exchange(ref _suppressed, 0));
    }
}

public sealed class WorldEntryGate(
    WorldId worldId,
    IWorldMaintenanceRepository maintenance,
    IAccountRepository accounts,
    TimeProvider? clock = null,
    ILogger<WorldEntryGate>? logger = null) : IWorldEntryGate
{
    private readonly EntryCheckFailureLog _failures = new(clock ?? TimeProvider.System);

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
        catch (Exception e)
        {
            _failures.Report(logger ?? (ILogger)NullLogger.Instance, "Reading the maintenance state or the account", e);
            return default;
        }
    }
}
