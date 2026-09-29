using Avalon.Common.Accounts;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Packets.Social;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Maintenance;

/// <summary>Applies persisted maintenance revisions; only Advance runs on the simulation tick.</summary>
public sealed class WorldMaintenanceCoordinator(
    WorldId worldId,
    IWorldMaintenanceRepository repository,
    ICharacterSaver saver,
    TimeProvider clock,
    ILogger<WorldMaintenanceCoordinator> logger)
{
    private static readonly int[] Thresholds = [180, 60, 30, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0];
    private readonly object _sync = new();
    private readonly List<Task> _closes = [];
    private readonly HashSet<IWorldConnection> _closing = new(ReferenceEqualityComparer.Instance);
    private WorldMaintenanceState? _state;
    private bool _startPending;
    private int _lastRemaining;
    private Func<bool> _disconnectsProcessed = static () => true;
    private Task? _drainTask;

    public WorldMaintenanceState? CurrentState
    {
        get { lock (_sync) return _state; }
    }

    /// <summary>Serializes the final spawn with locally applied maintenance transitions.</summary>
    public bool RunIfEntryAllowed(IWorldConnection connection, Action enter)
    {
        lock (_sync)
        {
            if (_state is null || (_state.Enabled &&
                (connection.AccessLevel & AccountAccessLevel.Admin) == 0))
                return false;
            enter();
            return true;
        }
    }

    public void SetDrainObserver(Func<bool> disconnectsProcessed)
        => _disconnectsProcessed = disconnectsProcessed;

    public async Task InitializeAsync(CancellationToken ct)
    {
        WorldMaintenanceState state = await repository.ReadAsync(worldId, ct)
            ?? throw new InvalidOperationException($"World {worldId.Value} has no maintenance row");
        lock (_sync)
        {
            if (_state is not null && _state.Revision > state.Revision) return;
            _state = state;
            _startPending = false;
            _lastRemaining = state.Enabled
                ? Math.Max(1, Remaining(state, clock.GetUtcNow().UtcDateTime)) : 0;
        }
    }

    public async Task ApplyNotificationAsync(long revision, CancellationToken ct)
    {
        lock (_sync)
            if (_state is not null && revision <= _state.Revision) return;
        await ReconcileAsync(ct);
    }

    public async Task ReconcileAsync(CancellationToken ct)
    {
        WorldMaintenanceState state = await repository.ReadAsync(worldId, ct)
            ?? throw new InvalidOperationException($"World {worldId.Value} has no maintenance row");
        ApplyCommitted(state);
    }

    public void ApplyCommitted(WorldMaintenanceState state)
    {
        lock (_sync)
        {
            if (_state is not null && state.Revision <= _state.Revision) return;
            _state = state;
            _startPending = state.Enabled;
            _lastRemaining = state.Enabled ? int.MaxValue : 0;
            _closing.Clear();
            logger.LogInformation("World {WorldId} maintenance revision {Revision}: {Enabled}, deadline {Deadline}",
                worldId.Value, state.Revision, state.Enabled, state.DeadlineUtc);
        }
    }

    public void Advance(DateTime nowUtc, IReadOnlyList<IWorldConnection> connections)
    {
        lock (_sync)
        {
            if (_state is not { Enabled: true } state) return;

            int remaining = Remaining(state, nowUtc);
            string? message = null;
            if (_startPending && remaining > 0)
            {
                message = $"World maintenance has started. Non-admin players will be disconnected in {Duration(remaining)}.";
                _startPending = false;
            }
            else
            {
                int? crossed = Thresholds.LastOrDefault(threshold => _lastRemaining > threshold && remaining <= threshold);
                if (crossed.HasValue && (_lastRemaining > crossed.Value && remaining <= crossed.Value))
                    message = $"World maintenance: {Duration(crossed.Value)} remaining. Non-admin players will be disconnected.";
            }

            _lastRemaining = remaining;
            if (message is not null)
                foreach (IWorldConnection connection in connections)
                    if (connection.IsConnected && !connection.IsClosing && connection.InGame)
                        try
                        {
                            connection.Send(SChatMessagePacket.Create(0, 0, "System", message, nowUtc,
                                connection.CryptoSession.Encrypt));
                        }
                        catch (Exception e)
                        {
                            logger.LogWarning(e, "World {WorldId} maintenance warning could not be sent to a client",
                                worldId.Value);
                        }

            if (remaining != 0) return;

            int newCloses = 0;
            foreach (IWorldConnection connection in connections)
            {
                if (!connection.IsConnected || connection.IsClosing || connection.AccountId is null ||
                    (connection.AccessLevel & AccountAccessLevel.Admin) != 0 || !_closing.Add(connection))
                    continue;

                try
                {
                    _closes.Add(GracefulShutdownHelper.NotifyAndCloseAsync(connection,
                        "World is under maintenance", DisconnectReason.Maintenance, logger));
                    newCloses++;
                }
                catch (Exception e)
                {
                    _closing.Remove(connection);
                    logger.LogWarning(e, "World {WorldId} maintenance close could not be started for a client",
                        worldId.Value);
                }
            }
            if (newCloses > 0 && (_drainTask is null || _drainTask.IsCompleted))
                _drainTask = Task.Run(DrainAndLogAsync);
        }
    }

    private async Task DrainAndLogAsync()
    {
        try
        {
            await WhenDrainedAsync(CancellationToken.None);
        }
        catch (Exception e)
        {
            logger.LogError(e, "World {WorldId} maintenance disconnect drain failed", worldId.Value);
        }
    }

    public async Task WhenDrainedAsync(CancellationToken ct)
    {
        int awaited = 0;
        while (true)
        {
            Task[] pending;
            lock (_sync) pending = _closes.Skip(awaited).ToArray();
            if (pending.Length > 0)
            {
                try
                {
                    await Task.WhenAll(pending).WaitAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception e)
                {
                    logger.LogError(e, "World {WorldId} maintenance close failed; waiting for saves",
                        worldId.Value);
                }
            }
            awaited += pending.Length;

            // The close event queues a despawn for the next tick; its save starts as that queue is
            // drained. Wait for that handoff before observing the saver's idle task.
            while (!_disconnectsProcessed())
                await Task.Delay(10, ct);
            await saver.WhenAllIdle().WaitAsync(ct);
            lock (_sync)
                if (_closes.Count == awaited) return;
        }
    }

    private static int Remaining(WorldMaintenanceState state, DateTime nowUtc)
        => Math.Max(0, (int)Math.Ceiling(((state.DeadlineUtc ?? nowUtc) - nowUtc).TotalSeconds));

    private static string Duration(int seconds) => seconds switch
    {
        >= 60 when seconds % 60 == 0 => $"{seconds / 60} minute{(seconds == 60 ? "" : "s")}",
        _ => $"{seconds} second{(seconds == 1 ? "" : "s")}",
    };
}
