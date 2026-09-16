using Avalon.Common.ValueObjects;
using Avalon.Hosting.Networking;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;

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
    ///     The character built by character-select, waiting to be spawned. Null once it has been
    ///     spawned, and null for a connection that has not selected. While this is set
    ///     <see cref="Character" /> is still null: the character exists but nothing on the tick
    ///     can see it.
    /// </summary>
    public PendingSpawn? PendingSpawn { get; }

    /// <summary>
    ///     True from the moment a character select is accepted until the entity is handed over as
    ///     a pending spawn, or until the chain gives up. <see cref="Character" /> and
    ///     <see cref="PendingSpawn" /> are BOTH null across that span, which is several database
    ///     round trips long, so this is the only thing that says a select is under way.
    /// </summary>
    public bool SelectInProgress { get; set; }

    /// <summary>
    ///     Whether the socket is still up. The tick reads it before acting on a connection's
    ///     pending spawn; a dropped connection's pending spawn belongs to the despawn.
    /// </summary>
    public bool IsConnected { get; }

    /// <summary>
    ///     Holds a built character out of its instance. Clears <see cref="SelectInProgress" />:
    ///     the pending spawn supersedes it. <paramref name="sinceTicks" /> is
    ///     <c>DateTime.UtcNow.Ticks</c> and starts the readiness barrier.
    /// </summary>
    void SetPendingSpawn(ICharacter character, IMapInstance instance, long sinceTicks);

    /// <summary>
    ///     Takes the pending spawn, clearing it. Returns null when there is none.
    /// </summary>
    PendingSpawn? TakePendingSpawn();

    /// <summary>
    ///     Gets the latency of the connection.
    /// </summary>
    public long Latency { get; }

    /// <summary>
    ///     Gets the round-trip time of the connection.
    /// </summary>
    public long RoundTripTime { get; }

    /// <summary>
    ///     Gets the tick at which the packet now being handled was read off the socket. A handler runs
    ///     on the world tick, so reading the clock inside one measures the wait for that tick as well.
    /// </summary>
    public long CurrentPacketArrivedTicks { get; }

    /// <summary>
    ///     Gets a value indicating whether the connection is in-game.
    /// </summary>
    public bool InGame { get; }

    /// <summary>
    ///     Gets a value indicating whether the connection is in a map.
    /// </summary>
    public bool InMap { get; }

    /// <summary>Gets or sets the last accepted input sequence number.</summary>
    public uint LastInputSeq { get; set; }

    /// <summary>
    ///     Raw <c>ObjectGuid</c> of the unit the player is currently targeting, or <c>null</c>
    ///     when no target is selected. Updated by <c>CMSG_TARGET_UNIT</c>; read by
    ///     <c>ThreatBroadcastService</c> to decide which encounter's threat list to mirror back
    ///     to this client via <c>SThreatListPacket</c>.
    /// </summary>
    public ulong? CurrentTargetGuid { get; set; }

    /// <summary>True between accepting a CMSG_RESPAWN_AT_TOWN and completing the transfer.
    /// Used by the respawn handler to drop concurrent respawn requests during the async
    /// resolver+instance-load chain.</summary>
    bool RespawnInFlight { get; set; }

    /// <summary>
    ///     Sends a single time-synchronization ping to the client.
    ///     Driven by <c>WorldServer</c>'s tick loop on a fixed cadence.
    /// </summary>
    void SendTimeSyncPing();

    /// <summary>
    ///     Asks for a time-sync ping on the next tick, whatever this connection's phase. A handler
    ///     cannot send one itself: SendTimeSyncPing stamps the send time the round trip is measured
    ///     against, and a handler runs at the top of a tick while the outbox is flushed at the bottom,
    ///     so the stamp would carry the world update between them.
    /// </summary>
    void RequestInitialTimeSyncPing();

    /// <summary>Takes that request if one is outstanding, clearing it. Called only from the tick.</summary>
    bool TakeInitialTimeSyncPingRequest();

    /// <summary>
    ///     Called when a pong response is received.
    /// </summary>
    /// <param name="lastServerTimestamp">The last server timestamp.</param>
    /// <param name="clientReceivedTimestamp">The client received timestamp.</param>
    /// <param name="clientSentTimestamp">The client sent timestamp.</param>
    /// <param name="serverReceivedTicks">When the pong was read off the socket, NOT when it was handled.</param>
    void OnPongReceived(long lastServerTimestamp, long clientReceivedTimestamp,
        long clientSentTimestamp, long serverReceivedTicks);

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
