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
    ///     Enables the time synchronization worker for the connection.
    /// </summary>
    void EnableTimeSyncWorker();

    /// <summary>
    ///     Called when a pong response is received.
    /// </summary>
    /// <param name="lastServerTimestamp">The last server timestamp.</param>
    /// <param name="clientReceivedTimestamp">The client received timestamp.</param>
    /// <param name="clientSentTimestamp">The client sent timestamp.</param>
    void OnPongReceived(long lastServerTimestamp, long clientReceivedTimestamp,
        long clientSentTimestamp);

    /// <summary>
    ///     Updates the connection with the specified delta time and packet filter.
    /// </summary>
    /// <param name="deltaTime">The delta time since the last update.</param>
    /// <param name="filter">The packet filter to use for the update.</param>
    void Update(TimeSpan deltaTime, PacketFilter filter);

    /// <summary>Queues an async result for safe execution on the tick thread.</summary>
    void EnqueueContinuation<T>(Task<T> task, Action<T> callback);

    /// <summary>Queues an async result for safe execution on the tick thread (no-result overload).</summary>
    void EnqueueContinuation(Task task, Action callback);
}
