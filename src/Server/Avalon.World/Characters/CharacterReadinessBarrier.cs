using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Generic;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Maintenance;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Characters;

/// <summary>
///     The gate between selecting a character and being in the world. Character-select builds the
///     entity and hands it to the connection as a pending spawn; this is what puts it in the
///     instance, either because the client reported the map loaded or because the wait expired.
/// </summary>
public static class CharacterReadinessBarrier
{
    private static readonly ConditionalWeakTable<IWorldConnection, Task<bool>> PendingChecks = new();

    /// <summary>Checks current admission off the tick before a pending character becomes visible.</summary>
    public static void RequestRelease(IWorldConnection connection, IWorld world, ILogger logger,
        IWorldEntryGate gate, Action? onSpawn = null, WorldMaintenanceCoordinator? maintenance = null)
    {
        if (!connection.IsConnected || connection.IsClosing || connection.AccountId is null ||
            connection.PendingSpawn is not { } pending || PendingChecks.TryGetValue(connection, out _))
            return;

        Task<bool> check = Task.Run(async () =>
        {
            try
            {
                return await gate.CheckAsync(connection.AccountId, CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception)
            {
                return false;
            }
        });
        PendingChecks.Add(connection, check);
        connection.EnqueueContinuation(check, allowed =>
        {
            PendingChecks.Remove(connection);
            if (!connection.IsConnected || connection.IsClosing || !ReferenceEquals(connection.PendingSpawn, pending))
                return;

            if (!allowed)
            {
                GracefulShutdownHelper.NotifyAndClose(connection, "World is under maintenance",
                    DisconnectReason.Maintenance, logger);
                return;
            }

            bool released = false;
            if (maintenance is not null)
            {
                if (!maintenance.RunIfEntryAllowed(connection,
                        () => released = Release(connection, world, logger)))
                {
                    GracefulShutdownHelper.NotifyAndClose(connection, "World is under maintenance",
                        DisconnectReason.Maintenance, logger);
                    return;
                }
            }
            else
                released = Release(connection, world, logger);

            if (released) onSpawn?.Invoke();
        });
    }

    /// <summary>
    ///     Assigns the pending character to its connection and spawns it. After a successful call
    ///     the entity is visible to <c>MapInstance.Update</c>.
    /// </summary>
    /// <returns>
    ///     False when nothing was pending, or when the spawn threw, in which case
    ///     <see cref="IWorldConnection.Character" /> is left null.
    /// </returns>
    public static bool Release(IWorldConnection connection, IWorld world, ILogger logger)
    {
        PendingSpawn? pending = connection.TakePendingSpawn();
        if (pending is null)
            return false;

        connection.Character = pending.Character;
        try
        {
            world.SpawnInInstance(connection, pending.Instance);
        }
        catch (Exception e)
        {
            connection.Character = null;
            // Handed back before the close. The despawn the close queues is what writes the row
            // back, and it can only find the character through a pending spawn.
            connection.SetPendingSpawn(pending.Character, pending.Instance, pending.SinceTicks);
            logger.LogError(e, "Error while spawning character {CharacterId} for account {AccountId}",
                pending.Character.Guid.Id, connection.AccountId);
            connection.Close();
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Spawns every connection that has been waiting on its client for longer than
    ///     <paramref name="timeout" />. A client that never reports its map loaded must not strand
    ///     a connected player outside the world.
    /// </summary>
    /// <param name="nowTicks"><c>DateTime.UtcNow.Ticks</c>.</param>
    public static void ReleaseExpired(IEnumerable<IWorldConnection> connections, IWorld world,
        long nowTicks, TimeSpan timeout, ILogger logger, IWorldEntryGate? gate = null,
        WorldMaintenanceCoordinator? maintenance = null)
    {
        foreach (IWorldConnection connection in connections)
        {
            // A dropped connection's pending spawn belongs to the despawn, not to this. Skipping it
            // is also what stops a spawn that threw being retried every tick: the failed release
            // closes the connection and hands the pending spawn back for the despawn to adopt.
            if (!connection.IsConnected)
                continue;

            if (connection.PendingSpawn is not { } pending)
                continue;

            TimeSpan waited = TimeSpan.FromTicks(nowTicks - pending.SinceTicks);
            if (waited < timeout)
                continue;

            // Read before the release: it takes the pending spawn.
            string characterName = pending.Character.Name;

            if (gate is not null)
            {
                RequestRelease(connection, world, logger, gate, () => logger.LogWarning(
                    "Character {CharacterName} for account {AccountId} spawned without a load report; " +
                    "the readiness barrier expired after {WaitedMs}ms",
                    characterName, connection.AccountId, (long)waited.TotalMilliseconds), maintenance);
                continue;
            }

            if (!CharacterReadinessBarrier.Release(connection, world, logger))
                continue;

            logger.LogWarning(
                "Character {CharacterName} for account {AccountId} spawned without a load report; " +
                "the readiness barrier expired after {WaitedMs}ms",
                characterName, connection.AccountId, (long)waited.TotalMilliseconds);
        }
    }

    /// <summary>
    ///     Ends every select that has been under way longer than <paramref name="timeout" /> without
    ///     producing a pending spawn.
    /// </summary>
    /// <remarks>
    ///     <see cref="ReleaseExpired" /> cannot cover this. It inspects connections that already
    ///     have a pending spawn, and a select whose chain died never produced one — it leaves
    ///     <c>SelectInProgress</c> true with both <c>Character</c> and <c>PendingSpawn</c> null,
    ///     which is the guard's "mid-select" state. Nothing cleared it, and every handler that gates
    ///     on selection state — select, create, delete, list — refuses from then on, so the player
    ///     could not touch their characters again without reconnecting.
    ///     <para>
    ///     A faulted continuation is the way in: <c>ProcessContinuations</c> logs and drops it, which
    ///     is right for the queue and leaves the chain with no step that will ever run.
    ///     </para>
    ///     The connection is closed rather than handed back to the character list: by the time a
    ///     late step can fault, the client already has SMSG_CHARACTER_SELECTED and its chunk layout
    ///     and is loading the map, so there is no earlier state for it to return to.
    /// </remarks>
    /// <param name="nowTicks"><c>DateTime.UtcNow.Ticks</c>.</param>
    public static void CancelExpiredSelects(IEnumerable<IWorldConnection> connections,
        long nowTicks, TimeSpan timeout, ILogger logger)
    {
        foreach (IWorldConnection connection in connections)
        {
            if (!connection.IsConnected)
                continue;

            // A select that reached a pending spawn is the readiness barrier's business, not this.
            if (!connection.SelectInProgress)
                continue;

            TimeSpan waited = TimeSpan.FromTicks(nowTicks - connection.SelectStartedTicks);
            if (waited < timeout)
                continue;

            logger.LogWarning(
                "Character select for account {AccountId} never finished; giving up after {WaitedMs}ms " +
                "and disconnecting, because a select left in progress blocks every later one",
                connection.AccountId, (long)waited.TotalMilliseconds);

            connection.CancelSelect();
            GracefulShutdownHelper.NotifyAndClose(
                connection, "Character select timed out", DisconnectReason.SelectTimeout, logger);
        }
    }
}
