using Avalon.Database.Auth.Repositories;

namespace Avalon.Server.Auth;

/// <summary>
/// The auth server's liveness sweep for <c>Accounts.Online</c> (#555). A connection's close clears
/// its account's flag, but a close whose write failed leaves the account marked online, and before
/// this only the next start-up's <c>MarkAllOfflineAsync</c> cleared it. Every
/// <c>Application:OnlineSweepIntervalSeconds</c> this clears the flag of each account marked online
/// whose session is none of this server's live connections.
/// <para>
/// Timed like the world server's timers: the due time is absolute (<see cref="TimeProvider"/>), each
/// pass checks it, and a pass that runs late sweeps once and schedules the next from now, not once
/// per interval it missed.
/// </para>
/// <para>
/// Exactly one auth server is supported (#487), and this relies on it just as the start-up reset
/// does: a session a second server's connection set is never live here, so this would clear it.
/// </para>
/// </summary>
public sealed class OnlineSweep(IAccountRepository accounts, Func<IEnumerable<Guid>> liveSessions, TimeSpan interval,
    TimeProvider time, ILogger logger)
{
    /// <summary>When the next sweep is due.</summary>
    public DateTimeOffset NextDue { get; private set; } = time.GetUtcNow() + interval;

    /// <summary>
    /// Sweeps if <see cref="NextDue"/> has come, and returns whether it did. A sweep that throws is
    /// logged and the next one is still scheduled.
    /// </summary>
    public async Task<bool> RunIfDueAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        if (now < NextDue)
            return false;

        NextDue = now + interval;
        try
        {
            await SweepAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The type only: a database exception's message can name the server it failed to reach.
            logger.LogError("The online liveness sweep failed with {ExceptionType}; the next one is due at {NextDue}",
                ex.GetType().FullName, NextDue);
        }

        return true;
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        // The rows are read before the live connections, on purpose: a connection is live from its
        // accept, before it can log in, so a session read here that is not live at the snapshot
        // below has already closed, and a connection id is never reused.
        IReadOnlyList<OnlineSession> online = await accounts.ListOnlineSessionsAsync(cancellationToken)
            .ConfigureAwait(false);
        if (online.Count == 0)
            return;

        var live = new HashSet<Guid>(liveSessions());
        foreach (OnlineSession session in online)
        {
            if (session.SessionId is { } id && live.Contains(id))
                continue;

            // Clears only while the row's session is still the one read, so a login that landed
            // since keeps its flag. No session time is added: when this session really ended is
            // unknown, and its close, if it ran, already counted it.
            await accounts.MarkOfflineAsync(session.AccountId, session.SessionId, 0, cancellationToken)
                .ConfigureAwait(false);
            logger.LogInformation("Cleared the online flag of account {AccountId}: its session is not live",
                session.AccountId.Value);
        }
    }

    /// <summary>
    /// Checks the due time every <paramref name="pollPeriod"/> until <paramref name="cancellationToken"/>
    /// is cancelled, then returns without throwing.
    /// </summary>
    public async Task RunAsync(TimeSpan pollPeriod, CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(pollPeriod, time);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                await RunIfDueAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown.
        }
    }
}
