using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Characters;

/// <summary>
///     The gate between selecting a character and being in the world. Character-select builds the
///     entity and hands it to the connection as a pending spawn; this is what puts it in the
///     instance, either because the client reported the map loaded or because the wait expired.
/// </summary>
public static class CharacterReadinessBarrier
{
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
        long nowTicks, TimeSpan timeout, ILogger logger)
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

            if (!CharacterReadinessBarrier.Release(connection, world, logger))
                continue;

            logger.LogWarning(
                "Character {CharacterName} for account {AccountId} spawned without a load report; " +
                "the readiness barrier expired after {WaitedMs}ms",
                characterName, connection.AccountId, (long)waited.TotalMilliseconds);
        }
    }
}
