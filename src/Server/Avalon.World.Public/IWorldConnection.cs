using Avalon.Common.ValueObjects;
using Avalon.Hosting.Networking;
using Avalon.World.Public.Characters;

namespace Avalon.World.Public;

/// <summary>
///     Represents a connection to the world server.
/// </summary>
public interface IWorldConnection : IConnection
{
    /// <summary>
    ///     Gets or sets the account ID associated with the connection.
    /// </summary>
    public AccountId? AccountId { get; set; }

    /// <summary>
    ///     Gets or sets the character associated with the connection.
    /// </summary>
    public ICharacter? Character { get; set; }

    /// <summary>
    ///     Gets the latency of the connection.
    /// </summary>
    public long Latency { get; }

    /// <summary>
    ///     Gets the round-trip time of the connection.
    /// </summary>
    public long RoundTripTime { get; }

    /// <summary>
    ///     Gets a value indicating whether the connection is in-game.
    /// </summary>
    public bool InGame { get; }

    /// <summary>
    ///     Gets a value indicating whether the connection is in a map.
    /// </summary>
    public bool InMap { get; }

    /// <summary>Gets or sets the last movement time of the connection.</summary>
    public double LastMovementTime { get; set; }

    /// <summary>
    ///     Sends a single time-synchronization ping to the client.
    ///     Driven by <c>WorldServer</c>'s tick loop on a fixed cadence.
    /// </summary>
    void SendTimeSyncPing();

    /// <summary>
    ///     Called when a pong response is received.
    /// </summary>
    /// <param name="lastServerTimestamp">The last server timestamp.</param>
    /// <param name="clientReceivedTimestamp">The client received timestamp.</param>
    /// <param name="clientSentTimestamp">The client sent timestamp.</param>
    void OnPongReceived(long lastServerTimestamp, long clientReceivedTimestamp,
        long clientSentTimestamp);

    /// <summary>
    ///     Processes pre-character packets (pong, character list/select/create/delete)
    ///     using the session filter. Called by <c>WorldServer</c> on every tick for all connections.
    /// </summary>
    void UpdateSession();

    /// <summary>
    ///     Processes in-map packets (movement, attack, chat) using the map filter.
    ///     Called by <c>MapInstance</c> on every tick for connections with an active character in a map.
    /// </summary>
    void UpdateMap();

    /// <summary>
    ///     Drains the continuation queue. Called by <c>WorldServer</c> once per tick after all
    ///     packet-processing passes complete, so continuations from both session and map passes
    ///     run exactly once per tick per connection.
    /// </summary>
    void FlushContinuations();

    /// <summary>
    ///     Serializes all queued outbound packets and writes them to the socket in one burst.
    ///     Called by <c>WorldServer.Update</c> once per tick after <c>_world.Update</c>.
    ///     No-op if no packets are queued or a write is already in flight.
    /// </summary>
    void FlushOutbox();

    /// <summary>Queues an async result for safe execution on the tick thread.</summary>
    void EnqueueContinuation<T>(Task<T> task, Action<T> callback);

    /// <summary>Queues an async result for safe execution on the tick thread (no-result overload).</summary>
    void EnqueueContinuation(Task task, Action callback);
}
