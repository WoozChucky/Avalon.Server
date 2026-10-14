using Avalon.Common.GameAuth;
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
    public bool IsTlsAuthenticated => _tlsAuthenticated;
    public GameSessionLease? GameSessionLease => Volatile.Read(ref _gameSessionLease);
    public bool IsGameplayAuthorized => _tlsAuthenticated && _protocolAccepted && !_maintenanceBlocked &&
        !IsClosing && GameSessionLease?.IsActive == true;

    // The tick's time as of this connection's last lease check (#977); none before the first. Tick only, as the check is.
    private TickTime _leaseCheckedAt;

    /// <summary>
    /// <see cref="IsGameplayAuthorized" /> as the tick asks it, at the start of each pass and for each packet (#977):
    /// the lease judged at the time the tick's lease check (<see cref="AdvanceGameplayLease" />, the first thing every
    /// tick does for every connection) was given, not by a clock read per pass and per packet. That check has already
    /// ended a lease within <see cref="GameAuthPolicy.SaveDrainMargin" /> of its end, so within the tick this differs
    /// from a fresh read only for a tick that outlasts the margin. A connection the tick has not checked yet (before its
    /// first tick) has no time, and the lease reads its clock, as the read loop's check does.
    /// </summary>
    private bool IsGameplayAuthorizedOnTick => _tlsAuthenticated && _protocolAccepted && !_maintenanceBlocked &&
        !IsClosing && GameSessionLease?.IsActiveAt(_leaseCheckedAt) == true;
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
    }

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
            if (heartbeat.IsCompletedSuccessfully)
            {
                SessionLeaseResponse reply = heartbeat.Result;
                if (reply.Error is null) { if (!lease.TryRenew(reply)) lease.Revoke(); }
                else if (reply.Error is not (GameAuthErrors.ServiceUnavailable or GameAuthErrors.BarrierPending))
                {
                    lease.Revoke();
                }
            }
        }
        // Leave time for the final save under the still-valid durable fence. No outage grants more time.
        if (!lease.IsActiveAt(now) || lease.RemainingAt(now) <= GameAuthPolicy.SaveDrainMargin)
        {
            lease.Revoke();
            BlockForMaintenance();
            CancelSelect();
            _gameplayDrain = ((WorldServer)Server).DrainGameplayAsync(this);
            Close(false);
            return;
        }
        if (_heartbeat is null && (_time.GetElapsedTime(_lastHeartbeatTicks, timestamp) >= GameAuthPolicy.HeartbeatInterval || Interlocked.Exchange(ref _revalidateGameplayLease, 0) != 0))
        {
            StartGameplayHeartbeat(lease, timestamp);
        }
    }

    // Capture only when a heartbeat is due, not on every tick that checks the lease.
    private void StartGameplayHeartbeat(GameSessionLease lease, long timestamp)
    {
        _lastHeartbeatTicks = timestamp;
        _heartbeat = WorldDatabaseWork.Admission.Run(() => ((WorldServer)Server).AdmissionClient.HeartbeatAsync(lease, CancellationToken.None));
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
