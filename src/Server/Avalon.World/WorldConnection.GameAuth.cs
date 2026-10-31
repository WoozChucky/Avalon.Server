using Avalon.Common.GameAuth;
using Avalon.Network.Packets.Generic;
using Avalon.World.GameAuth;
using Avalon.World.Persistence;
using Avalon.World.Threading;

namespace Avalon.World;

public partial class WorldConnection
{
    private volatile bool _tlsAuthenticated;
    private volatile bool _protocolAccepted;
    private int _admissionStarted;
    private GameSessionLease? _gameSessionLease;
    private Task<WorldAdmissionResult>? _admissionWork;
    private Task<SessionLeaseResponse>? _heartbeat;
    private int _revalidateGameplayLease;
    private long _lastHeartbeatTicks;
    public void RequestGameplayRevalidation() => Interlocked.Exchange(ref _revalidateGameplayLease, 1);
    private long _transportReadyTicks;
    private Task? _gameplayDrain;

    // Whether identity may hold the session past the lease held here (#1051, RenewalLedger): then its end is sent even
    // after this lease ran out. Written on the tick.
    private int _renewals;

    /// <summary>Whether identity may hold the session past <see cref="GameSessionLease" /> (#1051, <see cref="RenewalLedger" />).</summary>
    internal bool RenewalUnaccounted => RenewalLedger.MayOutlastLease(ref _renewals);

    // What identity is told when this connection's session ends (#967): the first reason noted, null while none. Written
    // by whichever thread closes, read when the session's end is built.
    private string? _sessionEndReason;

    /// <summary>Notes the first reason the world closed this connection for (#967), as <see cref="SessionEndReasons.For"/> maps it.</summary>
    public void NoteDisconnectReason(DisconnectReason reason) => NoteSessionEndReason(SessionEndReasons.For(reason));

    /// <summary>
    /// Notes the end reason itself (a <see cref="GameSessionEndReasons"/> value), before a close whose notice the client is
    /// told differently: the first noted wins, so the notice's own reason then changes nothing.
    /// </summary>
    internal void NoteSessionEndReason(string reason) => Interlocked.CompareExchange(ref _sessionEndReason, reason, null);

    /// <summary>
    /// The client said goodbye (<c>CMSG_LEAVE_WORLD</c>, #967): the session ends as a logout, which releases the place at
    /// once. Noted as the goodbye arrives, so no close after it changes what identity is told; a reason the world noted
    /// before it, as it was already closing the connection, stands.
    /// </summary>
    internal void NoteGoodbye() => NoteSessionEndReason(GameSessionEndReasons.Logout);

    /// <summary>What identity is told when this connection's session ends (#967): a drop when the world noted nothing.</summary>
    public string SessionEndReason => Volatile.Read(ref _sessionEndReason) ?? GameSessionEndReasons.Drop;
    public bool IsTlsAuthenticated => _tlsAuthenticated;
    public GameSessionLease? GameSessionLease => Volatile.Read(ref _gameSessionLease);
    public bool IsGameplayAuthorized => _tlsAuthenticated && _protocolAccepted && !_maintenanceBlocked &&
        !IsClosing && GameSessionLease?.IsActive == true;

    // The tick's time as of this connection's last lease check (#977); none before the first. Tick only, as the check is.
    private TickTime _leaseCheckedAt;

    /// <summary>
    /// The tick's monotonic time (#977) on <paramref name="clock" />, as this connection's lease check was given it at the
    /// start of the tick: what a handler that refills by time reads (the movement budget, #1267), so the clock is read
    /// once per tick, not once per packet. Before the first check, or on another clock, the clock is read now. Tick only.
    /// </summary>
    internal long TickTimestamp(TimeProvider clock) => _leaseCheckedAt.On(clock).Timestamp;

    // The lease that check found not due at _leaseCheckedAt, so active then (#977); null when it found none, or ended
    // it. Within the tick a renewal cannot shorten it and the time does not move, so only a revocation can end it.
    private GameSessionLease? _leaseNotDueAtCheck;

    /// <summary>
    /// <see cref="IsGameplayAuthorized" /> as the tick asks it, at the start of each pass and for each packet (#977):
    /// the lease judged at the time the tick's lease check (<see cref="AdvanceGameplayLease" />, the first thing every
    /// tick does for every connection) was given, not by a clock read per pass and per packet. That check has already
    /// ended a lease within <see cref="GameAuthPolicy.SaveDrainMargin" /> of its end, so within the tick this differs
    /// from a fresh read only for a tick that outlasts the margin, and the tick checks every lease again at a new time
    /// before its map pass when its session pass ran past the margin (WorldServer). The lease the check found not due
    /// is only asked whether it was revoked since: no lock, no comparison. Any other (none checked yet, before the
    /// connection's first tick, or published since) is judged at the checked time, or by its clock when there is none.
    /// </summary>
    private bool IsGameplayAuthorizedOnTick => _tlsAuthenticated && _protocolAccepted && !_maintenanceBlocked &&
        !IsClosing && GameSessionLease is { } lease &&
        (ReferenceEquals(lease, _leaseNotDueAtCheck) ? !lease.IsRevoked : lease.IsActiveAt(_leaseCheckedAt));
    public bool TryBeginAdmission() => _tlsAuthenticated && Interlocked.CompareExchange(ref _admissionStarted, 1, 0) == 0;
    public void TrackAdmission(Task<WorldAdmissionResult> work) => Volatile.Write(ref _admissionWork, work);
    internal Task<WorldAdmissionResult>? AdmissionWork => Volatile.Read(ref _admissionWork);

    // Tick continuation only. The trusted API result is the sole publisher of connection identity.
    public void PublishAdmission(GameSessionLease lease)
    {
        if (!_tlsAuthenticated || !lease.IsActive || Interlocked.CompareExchange(ref _gameSessionLease, lease, null) is not null)
            throw new InvalidOperationException("The world connection cannot be admitted.");
        BindGameplayAuthority(lease.Authority);
        AssignAccessLevel(lease.AccessLevel);
        AccountId = lease.Authority.AccountId;
        _lastHeartbeatTicks = _time.GetTimestamp();
        // The liveness clock starts at admission (#1034), so a client that never answers a ping is closed too.
        _lastAnswerTimestamp = _lastHeartbeatTicks;
    }

    /// <summary>Whether the version handshake has been accepted (<see cref="AcceptProtocol" />), which it is once.</summary>
    public bool IsProtocolAccepted => _protocolAccepted;

    public bool AcceptProtocol()
    {
        if (_protocolAccepted || !_tlsAuthenticated || GameSessionLease?.IsActive != true) return false;
        _protocolAccepted = true;
        return true;
    }

    /// <summary>
    /// Tick only. HTTP is owned by a thread-pool task; no network wait enters the simulation. <paramref name="now" /> is
    /// the tick's time, read once for every connection (#977); this tick's dispatches judge the lease at it too.
    /// </summary>
    public void AdvanceGameplayLease(in TickTime now)
    {
        _leaseCheckedAt = now;
        _leaseNotDueAtCheck = null;
        // The connection's own clock, which its timestamps were read from: the same as the tick's in production.
        long timestamp = now.On(_time).Timestamp;
        if (GameSessionLease is not { } lease)
        {
            if (_tlsAuthenticated && _time.GetElapsedTime(_transportReadyTicks, timestamp) >= GameAuthPolicy.AdmissionTimeout) Close(false);
            return;
        }
        if (_gameplayDrain is not null || IsClosing) return;
        if (_heartbeat is { IsCompleted: true } heartbeat)
        {
            _heartbeat = null;
            if (heartbeat.IsCompletedSuccessfully && heartbeat.Result.Error is null && lease.TryRenew(heartbeat.Result))
            {
                RenewalLedger.Applied(ref _renewals);
            }
            else
            {
                RenewalLedger.Unresolved(ref _renewals);
                if (heartbeat.IsCompletedSuccessfully &&
                    heartbeat.Result.Error is not (GameAuthErrors.ServiceUnavailable or GameAuthErrors.BarrierPending))
                {
                    lease.Revoke();
                }
            }
        }
        // Leave time for the final save under the still-valid durable fence. No outage grants more time.
        if (lease.DrainDueAt(now))
        {
            lease.Revoke();
            BlockForMaintenance();
            CancelSelect();
            _gameplayDrain = ((WorldServer)Server).DrainGameplayAsync(this);
            Close(false);
            return;
        }

        _leaseNotDueAtCheck = lease;
        if (_heartbeat is null && (_time.GetElapsedTime(_lastHeartbeatTicks, timestamp) >= GameAuthPolicy.HeartbeatInterval || Interlocked.Exchange(ref _revalidateGameplayLease, 0) != 0))
        {
            StartGameplayHeartbeat(lease, timestamp);
        }
    }

    // Capture only when a heartbeat is due, not on every tick that checks the lease. On the heartbeats' own lane (#1050):
    // a full admission lane, which a reconnect storm or a flood of tickets makes, never refuses one.
    private void StartGameplayHeartbeat(GameSessionLease lease, long timestamp)
    {
        _lastHeartbeatTicks = timestamp;
        RenewalLedger.Started(ref _renewals);
        _heartbeat = WorldDatabaseWork.Heartbeat.Run(() => ((WorldServer)Server).AdmissionClient.HeartbeatAsync(lease, CancellationToken.None));
    }

    internal Task DrainGameplayAsync()
    {
        BlockForMaintenance();
        CancelSelect();
        GameSessionLease?.Revoke();
        return _gameplayDrain ??= ((WorldServer)Server).DrainGameplayAsync(this);
    }
    internal Task WhenPendingOperationsIdle() => Task.WhenAll(_continuationQueue.Select(c => c.Work));
}
