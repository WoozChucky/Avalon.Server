using Avalon.Common;
using Avalon.Common.Accounts;
using Avalon.Common.GameAuth;
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
    AccountId? AccountId { get; set; }
    GameplayWriteAuthority? GameplayAuthority => null;
    bool IsGameplayAuthorized => false;

    /// <summary>
    ///     Gets or sets the character associated with the connection.
    /// </summary>
    ICharacter? Character { get; set; }

    /// <summary>
    ///     The character built by character-select, waiting to be spawned. Null once it has been
    ///     spawned, and null for a connection that has not selected. While this is set
    ///     <see cref="Character" /> is still null: the character exists but nothing on the tick
    ///     can see it.
    /// </summary>
    PendingSpawn? PendingSpawn { get; }

    /// <summary>
    ///     True from the moment a character select is accepted until the entity is handed over as
    ///     a pending spawn, or until the chain gives up. <see cref="Character" /> and
    ///     <see cref="PendingSpawn" /> are BOTH null across that span, which is several database
    ///     round trips long, so this is the only thing that says a select is under way.
    /// </summary>
    bool SelectInProgress { get; }

    /// <summary>
    ///     When the in-flight select began, as the UTC ticks of the world's <see cref="TimeProvider" />, or 0 when none is.
    ///     Taken from the caller rather than read from a clock here, the same way
    ///     <see cref="SetPendingSpawn" /> takes its <c>sinceTicks</c>, so the tick loop can decide a
    ///     select has stalled and a test can decide it without waiting.
    /// </summary>
    long SelectStartedTicks { get; }

    /// <summary>
    ///     True while a character leave (#663) is under way: from the moment the leave takes the
    ///     character out of the world until its logout save has finished and the answer is sent.
    ///     <see cref="Character" />, <see cref="PendingSpawn" /> and <see cref="SelectInProgress" />
    ///     are all clear across that span, so this is the only thing that says the connection is not
    ///     yet back at character selection. Read-only here; the server sets it.
    /// </summary>
    bool LeaveInProgress { get; }

    /// <summary>
    ///     True when the client reported its map loaded while its select was still in flight, before
    ///     the pending spawn existed to receive it. The select sends its last packet several database
    ///     round trips before it arms the spawn, so a quick client lands here; the barrier sweep then
    ///     releases the spawn on the next tick instead of waiting out the barrier. Cleared by
    ///     <see cref="BeginSelect" /> and by <see cref="TakePendingSpawn" />.
    /// </summary>
    bool LoadReportedEarly { get; }

    /// <summary>Records a load report that arrived during the select; see <see cref="LoadReportedEarly" />.</summary>
    void NoteLoadReportedEarly();

    /// <summary>
    ///     Marks a select as under way. <paramref name="nowTicks" /> is
    ///     the UTC ticks of the world's <see cref="TimeProvider" /> and starts the window a stalled select is cancelled after.
    /// </summary>
    void BeginSelect(long nowTicks);

    /// <summary>
    ///     Ends an in-flight select without a character. For the chain's own give-up paths, and for
    ///     the tick loop when a select has stalled past its window.
    /// </summary>
    void CancelSelect();

    /// <summary>
    ///     Whether the socket is still up. The tick reads it before acting on a connection's
    ///     pending spawn; a dropped connection's pending spawn belongs to the despawn.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    ///     Whether a close has been asked for. Set at once by the close, while
    ///     <see cref="IsConnected" /> stays true until queued packets have gone out, so a connection
    ///     that was kicked or is closing can still have packets dispatched. Read-only.
    /// </summary>
    bool IsClosing { get; }

    /// <summary>
    ///     Holds a built character out of its instance. Clears <see cref="SelectInProgress" />:
    ///     the pending spawn supersedes it. <paramref name="sinceTicks" /> is
    ///     the UTC ticks of the world's <see cref="TimeProvider" /> and starts the readiness barrier.
    /// </summary>
    void SetPendingSpawn(ICharacter character, IMapInstance instance, long sinceTicks);

    /// <summary>
    ///     Takes the pending spawn, clearing it. Returns null when there is none.
    /// </summary>
    PendingSpawn? TakePendingSpawn();

    /// <summary>
    ///     The last network round trip (<see cref="RoundTripTime" />) in whole milliseconds: never negative, and 0 until
    ///     the first pong that answers a ping. Advisory, in both directions, and nothing to base a decision about a
    ///     player on. It has no age: a client that stops answering keeps its last reading (until the liveness rule
    ///     closes it, a minute on), and one that never answers reads 0. A client can raise it by answering late. It can
    ///     lower it, as far as zero, by claiming to have held the ping for up to the whole round trip (#1034), or below
    ///     its real round trip by guessing the next ping's timestamp, which is a clock reading it can predict. It has no
    ///     setter; <see cref="OnPongReceived" /> changes it, from the arrival time its caller passes.
    /// </summary>
    long Latency { get; }

    /// <summary>
    ///     The last network round trip in ticks: from the server's stamp on a time-sync ping, taken as the ping was
    ///     written, to the arrival of the pong that echoes it, less the time the client says it held the ping when that
    ///     is no less than zero and no more than the whole (#1034). Never negative, and 0 until the first such pong.
    ///     Advisory, as <see cref="Latency" /> is: see there.
    /// </summary>
    long RoundTripTime { get; }

    /// <summary>
    ///     Gets the tick at which the packet now being handled was read off the socket. A handler runs
    ///     on the world tick, so reading the clock inside one measures the wait for that tick as well.
    /// </summary>
    long CurrentPacketArrivedTicks { get; }

    /// <summary>
    ///     Gets a value indicating whether the connection is in-game.
    /// </summary>
    bool InGame { get; }

    /// <summary>
    ///     Gets a value indicating whether the connection is in a map.
    /// </summary>
    bool InMap { get; }

    /// <summary>Gets or sets the last accepted input sequence number.</summary>
    uint LastInputSeq { get; set; }

    /// <summary>
    ///     Raw <c>ObjectGuid</c> of the unit the player is currently targeting, or <c>null</c>
    ///     when no target is selected. Updated by <c>CMSG_TARGET_UNIT</c>; read by
    ///     <c>ThreatBroadcastService</c> to decide which encounter's threat list to mirror back
    ///     to this client via <c>SThreatListPacket</c>.
    /// </summary>
    ulong? CurrentTargetGuid { get; set; }

    /// <summary>
    ///     The account's locale, read once during character-select and cached. Defaults to enUS, so
    ///     dialogue works in English even if the read has not landed or failed.
    /// </summary>
    AccountLocale Locale { get; set; }

    /// <summary>
    ///     The account's access level, read once during character-select. Read-only here because
    ///     this interface is part of the future modding API: a setter would let any mod make any
    ///     player a GM. The server assigns it through <c>IAccessLevelAssignable</c> in Avalon.World.
    ///     Defaults to Player, so an unread level fails closed.
    /// </summary>
    AccountAccessLevel AccessLevel { get; }

    /// <summary>
    ///     The NPC and the node this connection is currently being shown, or null when no
    ///     conversation is open. The node id is what lets the server reject a choice made against a
    ///     node it is no longer showing.
    /// </summary>
    /// <remarks>
    ///     Invariant: a non-null value here implies <c>Character</c> is also non-null — a
    ///     conversation cannot be opened without a live character. <c>World.DeSpawnPlayerAsync</c>
    ///     relies on this to clear it below its own early-return-on-null-character guards; if that
    ///     invariant ever stops holding, those guards start leaking a stale conversation across a
    ///     reconnect and no existing test would catch it.
    /// </remarks>
    (ObjectGuid Npc, DialogueNodeId Node)? CurrentDialogue { get; set; }

    /// <summary>True between accepting a CMSG_RESPAWN_AT_TOWN and completing the transfer.
    /// Used by the respawn handler to drop concurrent respawn requests during the async
    /// resolver+instance-load chain.</summary>
    bool RespawnInFlight { get; set; }

    /// <summary>
    ///     Queues a time-sync ping; its send thread stamps it as it writes it (#875).
    ///     Driven by <c>WorldServer</c>'s tick loop on a fixed cadence (every five seconds) and the start burst.
    /// </summary>
    void SendTimeSyncPing();

    /// <summary>
    ///     Asks for the start burst of time-sync pings (#1034): one on the next tick, whatever this connection's phase,
    ///     and two more about a second apart.
    /// </summary>
    void RequestInitialTimeSyncPing();

    /// <summary>
    ///     Whether a ping of the start burst is due on <paramref name="tick" />, the world's ping tick count, and takes
    ///     it if so. Called only from the tick, once per tick, which moves the burst on.
    /// </summary>
    bool TakeTimeSyncPingRequest(long tick);

    /// <summary>
    ///     Handles a pong (#892). It counts only when <paramref name="lastServerTimestamp" /> is the stamp of a ping
    ///     this connection's send thread wrote and no pong has answered yet; the stamp is then spent. From the four
    ///     times of the exchange (#1034): the whole round trip is <paramref name="serverReceivedTicks" /> minus that
    ///     stamp, both the server's own; the network round trip is that less the client's hold of the ping
    ///     (<paramref name="clientSentTimestamp" /> minus <paramref name="clientReceivedTimestamp" />), unless the hold is
    ///     negative or longer than the whole round trip, when it is ignored; and the clock offset, server minus client,
    ///     is the stamp plus half the network round trip minus <paramref name="clientReceivedTimestamp" />. Any other
    ///     pong (an echo of no ping, a second answer to one ping, one that arrived before its stamp) changes nothing and
    ///     is not answered. The world calls this for a client's pong at most once per ping it wrote: a pong beyond that
    ///     is dropped as it arrives, before any handler.
    /// </summary>
    /// <param name="lastServerTimestamp">The echo of a ping's server timestamp: which ping this answers, nothing more.</param>
    /// <param name="clientReceivedTimestamp">When the client says it received the ping, on its own clock.</param>
    /// <param name="clientSentTimestamp">When the client says it sent the pong, on the same clock.</param>
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

    /// <summary>Queues an async result for safe execution on the tick thread.</summary>
    void EnqueueContinuation<T>(Task<T> task, Action<T> callback);

    /// <summary>Queues an async result for safe execution on the tick thread (no-result overload).</summary>
    void EnqueueContinuation(Task task, Action callback);
}
