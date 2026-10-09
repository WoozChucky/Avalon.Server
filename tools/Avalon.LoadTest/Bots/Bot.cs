using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using Avalon.LoadTest.Api;
using Avalon.LoadTest.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Movement;

namespace Avalon.LoadTest.Bots;

/// <summary>Where a bot is on its way into the world and out of it.</summary>
public enum BotState { SignedOut, SignedIn, Ticketed, Admitted, Handshaken, Selecting, Loaded, InWorld, Leaving, Stopped }

/// <summary>The last <c>SMSG_PLAYER_STATE_ACK</c> a bot's connection received: the input it answered and where it left the character.</summary>
public readonly record struct BotAck(uint Seq, float X, float Z, float VelX, float VelZ);

/// <summary>
/// One load-test bot: one account, signed in once, entering the run's world as the game client does (join ticket,
/// TLS, admission, handshake, character list, create on first entry, select, load report) and leaving it. Its
/// character is named after the account, with the class and gender its index gives. Entry and leave are driven by one
/// caller at a time; <see cref="NextInput"/>, <see cref="SendAsync"/> and the properties may be used by another (the
/// input driver) while the bot is <see cref="BotState.InWorld"/>. Disposed once nothing uses it any more: after its
/// leave, its life loop and the refresher are done.
/// </summary>
public sealed class Bot(int index, string account, string password, ApiClient api, ushort worldId, string? dialHost,
    BotMetrics metrics) : IDisposable
{
    /// <summary>The version the handshake sends: the protocol the provider attempt names.</summary>
    public const string ClientVersion = ApiClient.ProtocolVersion;

    /// <summary>The waits before the retries of a failed entry; one first attempt and a retry after each.</summary>
    private static readonly TimeSpan[] s_entryBackoff = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(9)];

    /// <summary>A join ticket lives 30 s; the call itself retries a timeout twice.</summary>
    private static readonly TimeSpan s_joinTimeout = TimeSpan.FromSeconds(60);

    /// <summary>The world allows 5 s for TLS; the dial gets as long again.</summary>
    private static readonly TimeSpan s_connectTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The world closes a connection not admitted within 15 s of TLS.</summary>
    private static readonly TimeSpan s_admissionTimeout = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan s_handshakeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>List, create and select are database round trips; select also waits for another session's save.</summary>
    private static readonly TimeSpan s_characterTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The world's <c>Game:CharacterLoadTimeoutSeconds</c> (15 s, <c>GameConfiguration</c> and the world's appsettings).
    /// It bounds the select's loads after its reply (quests, ignores, auras, read off the tick one after the other) until
    /// the spawn is armed, counted from the select's start, past which the world closes the connection. It is also the
    /// readiness barrier's own wait, which only a lost load report runs into: the bot's report, sent at once after the
    /// reply, reaches the world before the spawn is armed, is held, and is released by the first barrier sweep after
    /// the spawn is armed, a tick later, without waiting the barrier out.
    /// </summary>
    private static readonly TimeSpan s_worldLoadTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The wait for the spawn, from the moment the select reply is read: a safe upper bound, not the expected wait. The
    /// expected spawn is the select's loads after its reply (at most <see cref="s_worldLoadTimeout"/>), the barrier
    /// sweep's release a tick later, and its admission check. The bound adds the barrier's full wait as well, in case
    /// the report is lost, then 5 s for the admission check and the tick. Only past it is a missing spawn a
    /// <c>spawn:timeout</c>.
    /// </summary>
    private static readonly TimeSpan s_spawnTimeout = s_worldLoadTimeout + s_worldLoadTimeout + TimeSpan.FromSeconds(5);

    /// <summary>A character in the world has every input answered within a tick or two.</summary>
    private static readonly TimeSpan s_firstAckTimeout = TimeSpan.FromSeconds(10);

    /// <summary>A leave waits for the character's logout save.</summary>
    private static readonly TimeSpan s_leaveTimeout = TimeSpan.FromSeconds(20);

    /// <summary>The context's sign-out at the end of a leave, on its own clock: it runs even after the leave was cancelled.</summary>
    private static readonly TimeSpan s_logoutTimeout = TimeSpan.FromSeconds(5);

    /// <summary>How often a spawned bot repeats its first idle input until one is answered.</summary>
    private static readonly TimeSpan s_firstAckProbeInterval = TimeSpan.FromMilliseconds(50);

    private readonly Lock _ackLock = new();
    private readonly CancellationTokenSource _gaveUp = new();

    /// <summary>Guards <see cref="Context"/>'s last change: the leave's sign-out against a re-sign-in landing after it.</summary>
    private readonly Lock _contextLock = new();

    /// <summary>Set by the leave's sign-out; a context signed in after it is signed out at once. Guarded by <see cref="_contextLock"/>.</summary>
    private bool _signedOut;
    private volatile GameContext? _context;
    private volatile BotState _state = BotState.SignedOut;
    private volatile WorldConnection? _connection;
    private volatile TaskCompletionSource? _firstAck;
    private Action<NetworkPacket>? _onAck;
    private BotAck _lastAck;
    private uint _seq;

    /// <summary>The bot's index in the run: it picks the account, the class (<c>index % 4 + 1</c>) and the gender (<c>index % 2</c>).</summary>
    public int Index => index;

    /// <summary>The account's username, which is also the character's name.</summary>
    public string Account => account;

    /// <summary>The bot's state; <see cref="BotState.Stopped"/> for good once it gave up (<see cref="SignInAgainAsync"/>).</summary>
    public BotState State => _gaveUp.IsCancellationRequested ? BotState.Stopped : _state;

    /// <summary>Cancelled when the bot gives up for good: whatever it is doing for itself stops.</summary>
    public CancellationToken GaveUp => _gaveUp.Token;

    /// <summary>What the bot does in the world; set before it enters.</summary>
    public BehaviourKind Behaviour { get; set; }

    /// <summary>The input driver's state for this bot.</summary>
    internal InputLane Lane { get; } = new();

    /// <summary>The bot's game context once signed in, which its refresher keeps alive; null when signed out.</summary>
    public GameContext? Context
    {
        get => _context;
        private set => _context = value;
    }

    /// <summary>The last ack the current connection received (all zero before the first).</summary>
    public BotAck LastAck
    {
        get
        {
            lock (_ackLock)
                return _lastAck;
        }
    }

    /// <summary>The current connection's <see cref="WorldConnection.Closed"/>; a completed task when there is none.</summary>
    public Task Closed => _connection?.Closed ?? Task.CompletedTask;

    /// <summary>Called with each step's name and how long it took (for <c>check</c>).</summary>
    public Action<string, TimeSpan>? StepTimed { get; set; }

    /// <summary>Called with a line about a failed attempt that will be retried (for <c>check</c>).</summary>
    public Action<string>? Note { get; set; }

    /// <summary>
    /// Signs the account in as the launcher and the game client do. Two password checks on the API: once per ramp, the
    /// context is refreshed after. A failure is counted as a sign-in failure and thrown as a <see cref="BotStepException"/>.
    /// </summary>
    public async Task SignInAsync(CancellationToken ct)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            Context = await api.SignInAsync(account, password, ct);
        }
        catch (ApiException error)
        {
            metrics.SignInFailed(error.Step);
            throw new BotStepException("sign-in", $"sign-in:{error.Step}", error.Message);
        }

        _state = BotState.SignedIn;
        StepTimed?.Invoke("sign-in", Stopwatch.GetElapsedTime(start));
    }

    /// <summary>
    /// Replaces a game context that can no longer be refreshed (revoked, its refresh token spent) with a fresh sign-in,
    /// leaving the bot where it is: a bot thrown out of the world with the old context enters again with the new one.
    /// A failure is counted as a sign-in failure and the bot gives up for good: it has no context, it is
    /// <see cref="BotState.Stopped"/>, and <see cref="GaveUp"/> is cancelled. False then. A sign-in that completes
    /// after the bot's leave signed it out is signed out too, and not kept: false.
    /// </summary>
    public async Task<bool> SignInAgainAsync(CancellationToken ct)
    {
        try
        {
            GameContext fresh = await api.SignInAsync(account, password, ct);
            lock (_contextLock)
            {
                if (!_signedOut)
                {
                    Context = fresh;
                    return true;
                }
            }

            await SignOutAsync(fresh);
            return false;
        }
        catch (ApiException error)
        {
            metrics.SignInFailed(error.Step);
            Note?.Invoke($"Signing in again failed: {error.Message}; the bot stops.");
            // Given up first, then the context cleared: what the bot does for itself (an entry, linked to GaveUp) sees
            // the cancel before it can find no context, which it would count as an unexpected failure.
            await _gaveUp.CancelAsync();
            Context = null;
            return false;
        }
    }

    /// <summary>
    /// Enters the world: join ticket, connection, admission, handshake, list, create when the character is missing,
    /// select, load report, and the first input answered. A failed attempt is counted by kind and retried up to three
    /// times after 1 s, 3 s and 9 s, the retries taking over any session the failed attempt left; the last failure is
    /// thrown as a <see cref="BotStepException"/>. <paramref name="takeover"/> lets the first attempt replace the
    /// account's live world session.
    /// </summary>
    public async Task EnterAsync(bool takeover, CancellationToken ct)
    {
        if (Context is null) throw new InvalidOperationException($"Bot {index} is not signed in.");

        // A bot that gives up (SignInAgainAsync) stops entering at once, as on a cancel, and nothing is counted; for a
        // caller whose own token was not cancelled, that is a failed step of its own.
        using var entry = CancellationTokenSource.CreateLinkedTokenSource(ct, _gaveUp.Token);
        try
        {
            await EnterWithRetriesAsync(takeover, entry.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && _gaveUp.IsCancellationRequested)
        {
            throw new BotStepException("sign-in", "sign-in:gave-up", "the bot gave up: its game context could not be renewed");
        }
    }

    private async Task EnterWithRetriesAsync(bool takeover, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            metrics.EntryAttempt();
            try
            {
                await EnterOnceAsync(takeover || attempt > 0, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Stopped, not failed: nothing is counted, and no socket is left open.
                await CloseAsync();
                _state = BotState.SignedIn;
                throw;
            }
            catch (Exception error) when (error is not BotStepException)
            {
                // Something no step maps: counted and closed like any failure, and not retried.
                metrics.EntryFailed("entry:unexpected");
                await CloseAsync();
                _state = BotState.SignedIn;
                throw;
            }
            catch (BotStepException error)
            {
                metrics.EntryFailed(error.Kind);
                await CloseAsync();
                _state = BotState.SignedIn;
                if (attempt == s_entryBackoff.Length) throw;

                Note?.Invoke($"Attempt {attempt + 1} failed at {error.Step}: {error.Reason}; retrying in {s_entryBackoff[attempt].TotalSeconds:0} s.");
                await Task.Delay(s_entryBackoff[attempt], ct);
            }
        }
    }

    /// <summary>
    /// Change Character on the same connection: leave, wait for the logout save (<c>Left</c>), then list, select, load
    /// and the first input answered. Counted as an entry attempt, its entry time from the leave to the first ack; a
    /// failure is thrown as a <see cref="BotStepException"/> and leaves the connection to the caller.
    /// </summary>
    public async Task ChangeCharacterAsync(CancellationToken ct)
    {
        WorldConnection connection = _connection ?? throw new InvalidOperationException($"Bot {index} is not in the world.");
        metrics.EntryAttempt();
        long start = Stopwatch.GetTimestamp();
        try
        {
            _state = BotState.Selecting;
            await LeaveCharacterAsync(connection, ct);
            await SelectAsync(connection, ct);
            metrics.EntrySucceeded(Stopwatch.GetElapsedTime(start));
        }
        catch (BotStepException error)
        {
            metrics.EntryFailed(error.Kind);
            throw;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            metrics.EntryFailed("change:unexpected");
            throw;
        }
    }

    /// <summary>
    /// Leaves the world and keeps the game context, for a reconnect: the character leaves (its logout save commits),
    /// then the connection closes. Best effort: the world ends the session on a closed connection anyway.
    /// </summary>
    public async Task DisconnectAsync(CancellationToken ct)
    {
        if (_connection is { } connection)
        {
            BotState was = _state;
            _state = BotState.Leaving;
            try
            {
                // A leave is answered only once the handshake is done; before it the world ignores it.
                if (was is BotState.Selecting or BotState.Loaded or BotState.InWorld && !connection.Closed.IsCompleted)
                    await LeaveCharacterAsync(connection, ct);
            }
            catch (BotStepException error)
            {
                Note?.Invoke($"Leave: {error.Reason}; closing the connection anyway.");
            }
            finally
            {
                // Whatever happened to the leave, a cancel included, the socket is closed.
                await CloseAsync();
                _state = Context is null ? BotState.SignedOut : BotState.SignedIn;
            }
        }
    }

    /// <summary>
    /// Leaves the world (<see cref="DisconnectAsync"/>) and signs the game context out. The sign-out runs however the
    /// leave ended, a cancel included, on its own 5 s timeout rather than <paramref name="ct"/>.
    /// </summary>
    public async Task LeaveAsync(CancellationToken ct)
    {
        try
        {
            await DisconnectAsync(ct);
        }
        finally
        {
            await LogoutAsync();
        }
    }

    /// <summary>Disposes what the bot keeps for its life: the <see cref="GaveUp"/> source.</summary>
    public void Dispose() => _gaveUp.Dispose();

    /// <summary>The next input number on the current connection; they restart at 1 on each connection.</summary>
    public uint NextSeq() => Interlocked.Increment(ref _seq);

    /// <summary>
    /// A sealed <c>CMSG_PLAYER_INPUT</c> for the current connection, its send time noted for the ack latency: send it
    /// at once with <see cref="SendAsync"/>. Only while <see cref="BotState.InWorld"/>, so the input driver is the only
    /// sender of inputs: entry and Change Character send their own, and an <see cref="InvalidOperationException"/> here
    /// means the bot left the world since the caller looked.
    /// </summary>
    public NetworkPacket NextInput(uint seq, float dirX, float dirZ, ushort yaw)
    {
        if (_state != BotState.InWorld) throw new InvalidOperationException($"Bot {index} is not in the world.");
        WorldConnection connection = _connection ?? throw new InvalidOperationException($"Bot {index} has no connection.");
        return SealInput(connection, seq, dirX, dirZ, yaw);
    }

    /// <summary>Sends a packet on the current connection.</summary>
    public ValueTask SendAsync(NetworkPacket packet, CancellationToken ct) =>
        (_connection ?? throw new InvalidOperationException($"Bot {index} has no connection.")).SendAsync(packet, ct);

    /// <summary>
    /// Signs the game context out, best effort; the bot is <see cref="BotState.Stopped"/> after, with no context, and a
    /// re-sign-in still on its way signs its own context out when it lands.
    /// </summary>
    private async Task LogoutAsync()
    {
        GameContext? context;
        lock (_contextLock)
        {
            // The context is not used again: what the logout does not end, its 5-minute expiry does.
            context = Context;
            Context = null;
            _signedOut = true;
        }

        if (context is not null) await SignOutAsync(context);
        _state = BotState.Stopped;
    }

    /// <summary>Signs <paramref name="context"/> out on its own 5 s timeout; a failure is noted, never thrown.</summary>
    private async Task SignOutAsync(GameContext context)
    {
        long start = Stopwatch.GetTimestamp();
        using var limit = new CancellationTokenSource(s_logoutTimeout);
        try
        {
            await api.LogoutAsync(context, limit.Token);
            StepTimed?.Invoke("logout", Stopwatch.GetElapsedTime(start));
        }
        catch (ApiException error)
        {
            Note?.Invoke($"Logout: {error.Message}.");
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested)
        {
            Note?.Invoke($"Logout: no reply within {s_logoutTimeout.TotalSeconds:0} s.");
        }
    }

    private NetworkPacket SealInput(WorldConnection connection, uint seq, float dirX, float dirZ, ushort yaw)
    {
        NetworkPacket packet = connection.Seal(
            new CPlayerInputPacket { Seq = seq, DirX = dirX, DirZ = dirZ, YawDeg = yaw }, NetworkPacketType.CMSG_PLAYER_INPUT);
        metrics.InputSent(index, seq, Stopwatch.GetTimestamp());
        return packet;
    }

    private async Task EnterOnceAsync(bool takeover, CancellationToken ct)
    {
        GameContext? context = Context;
        if (context is null)
        {
            // The bot gave up since the entry began: SignInAgainAsync cancels GaveUp, which ct is linked to, before it
            // clears the context, so this is a stop rather than a failure.
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException($"Bot {index} lost its game context while entering.");
        }

        JoinTicket ticket = await StepAsync("join", s_joinTimeout,
            token => api.JoinTicketAsync(context, worldId, takeover, token), ct);
        // The client asked for the run's world and checked the reply; a destination elsewhere is never dialled.
        if (ticket.Destination.WorldId != worldId)
            throw new BotStepException("join", "join:wrong-world", $"the ticket is for world {ticket.Destination.WorldId}");

        long issued = Stopwatch.GetTimestamp();
        _state = BotState.Ticketed;

        WorldConnection connection = await StepAsync("connect", s_connectTimeout,
            token => WorldConnection.ConnectAsync(ticket.Destination, dialHost, token), ct);
        _seq = 0;
        lock (_ackLock)
            _lastAck = default;
        metrics.ForgetPending(index);
        _onAck = packet => OnAck(connection, packet);
        connection.Ack += _onAck;
        _connection = connection;

        await StepAsync("admission", s_admissionTimeout, async token =>
        {
            await connection.AdmitAsync(ticket.Ticket, token);
            return true;
        }, ct);
        _state = BotState.Admitted;

        await StepAsync("handshake", s_handshakeTimeout, async token =>
        {
            await connection.HandshakeAsync(ClientVersion, token);
            return true;
        }, ct);
        _state = BotState.Handshaken;

        await SelectAsync(connection, ct);
        TimeSpan entry = Stopwatch.GetElapsedTime(issued);
        metrics.EntrySucceeded(entry);
        StepTimed?.Invoke("entry", entry);
    }

    /// <summary>List, create when the character is missing, select, report loaded, and wait until an input is answered.</summary>
    private async Task SelectAsync(WorldConnection connection, CancellationToken ct)
    {
        _state = BotState.Selecting;
        CharacterInfo? character = await ListAsync(connection, ct);
        if (character is null)
        {
            SCharacterCreateResult result = await StepAsync("create", s_characterTimeout, async token =>
            {
                NetworkPacket reply = await connection.RequestAsync(connection.Seal(
                    new CCharacterCreatePacket { Name = account, Class = index % 4 + 1, Gender = index % 2 },
                    NetworkPacketType.CMSG_CHARACTER_CREATE), NetworkPacketType.SMSG_CHARACTER_CREATED, token);
                return connection.Codec.Decode<SCharacterCreatedPacket>(reply).Result;
            }, ct);

            // NameAlreadyExists: an earlier attempt made it and its answer was lost; the list shows it.
            if (result is not (SCharacterCreateResult.Success or SCharacterCreateResult.NameAlreadyExists))
                throw new BotStepException("create", $"create:{result}", $"the world refused to create {account}: {result}");

            character = await ListAsync(connection, ct)
                ?? throw new BotStepException("create", $"create:{result}", $"{account} is not on the character list after a create answered {result}");
        }

        uint characterId = character.CharacterId;
        (Task spawned, long selectRead) = await StepAsync("select", s_characterTimeout, async token =>
        {
            await connection.RequestAsync(connection.Seal(new CCharacterSelectedPacket { CharacterId = characterId },
                NetworkPacketType.CMSG_CHARACTER_SELECTED), NetworkPacketType.SMSG_CHARACTER_SELECTED, token);
            long read = Stopwatch.GetTimestamp();
            // Armed before the load report: nothing of the world reaches a connection before its character spawns.
            Task signal = connection.ArmSpawnSignal();
            // At once: the world holds the spawn until the client reports it has loaded (its barrier otherwise).
            await connection.SendAsync(connection.Seal(new CCharacterLoadedPacket(),
                NetworkPacketType.CMSG_CHARACTER_LOADED), token);
            return (signal, read);
        }, ct);
        _state = BotState.Loaded;

        // No input before the spawn is seen: one sent earlier would reach the world before its character does. The wait
        // counts from the select reply: the world arms the spawn only after more loads, then releases the held report.
        TimeSpan spawnLeft = s_spawnTimeout - Stopwatch.GetElapsedTime(selectRead);
        await StepAsync("spawn", spawnLeft > TimeSpan.Zero ? spawnLeft : TimeSpan.Zero, async token =>
        {
            Task first = await Task.WhenAny(spawned, connection.Closed).WaitAsync(token);
            if (first != spawned) throw new WorldClosedException("the connection closed before the character spawned");
            return true;
        }, ct, timeoutReason: $"no spawn within {s_spawnTimeout.TotalSeconds:0} s of the select reply (the world's loads " +
            $"after it and, were the load report lost, its readiness barrier, {s_worldLoadTimeout.TotalSeconds:0} s each, " +
            "and a margin)");

        await StepAsync("first-ack", s_firstAckTimeout, async token =>
        {
            await WaitForFirstAckAsync(connection, token);
            return true;
        }, ct);
        metrics.ForgetPending(index);
        _state = BotState.InWorld;
    }

    /// <summary>The account's character from a fresh list, or null when it has none of that name.</summary>
    private Task<CharacterInfo?> ListAsync(WorldConnection connection, CancellationToken ct) =>
        StepAsync("list", s_characterTimeout, async token =>
        {
            NetworkPacket reply = await connection.RequestAsync(
                connection.Seal(new CCharacterListPacket(), NetworkPacketType.CMSG_CHARACTER_LIST),
                NetworkPacketType.SMSG_CHARACTER_LIST, token);
            CharacterInfo[]? characters = connection.Codec.Decode<SCharacterListPacket>(reply).Characters;
            return characters?.FirstOrDefault(c => string.Equals(c.Name, account, StringComparison.OrdinalIgnoreCase));
        }, ct);

    /// <summary>
    /// After the spawn is seen: an idle input, repeated until one is answered. The world answers every input of a
    /// character in an instance, so the first ack is the moment the bot is in the world.
    /// </summary>
    private async Task WaitForFirstAckAsync(WorldConnection connection, CancellationToken ct)
    {
        var firstAck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _firstAck = firstAck;
        try
        {
            while (!firstAck.Task.IsCompleted)
            {
                await connection.SendAsync(SealInput(connection, NextSeq(), 0f, 0f, 0), ct);
                Task closed = connection.Closed;
                Task first = await Task.WhenAny(firstAck.Task, closed, Task.Delay(s_firstAckProbeInterval, ct));
                if (first == closed) throw new WorldClosedException("the connection closed before an input was answered");
                ct.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            _firstAck = null;
        }
    }

    private async Task LeaveCharacterAsync(WorldConnection connection, CancellationToken ct)
    {
        CharacterLeaveResult result = await StepAsync("leave", s_leaveTimeout, async token =>
        {
            NetworkPacket reply = await connection.RequestAsync(
                connection.Seal(new CCharacterLeavePacket(), NetworkPacketType.CMSG_CHARACTER_LEAVE),
                NetworkPacketType.SMSG_CHARACTER_LEAVE_RESULT, token);
            return connection.Codec.Decode<SCharacterLeaveResultPacket>(reply).Result;
        }, ct);
        if (result != CharacterLeaveResult.Left)
            throw new BotStepException("leave", $"leave:{result}", $"the world answered the leave with {result}");
    }

    /// <summary>An ack on <paramref name="connection"/>'s read loop, decoded with its session; ignored once it is not the bot's.</summary>
    private void OnAck(WorldConnection connection, NetworkPacket packet)
    {
        if (!ReferenceEquals(connection, _connection)) return;

        long arrived = Stopwatch.GetTimestamp();
        SPlayerStateAckPacket ack = connection.Codec.Decode<SPlayerStateAckPacket>(packet);
        metrics.AckReceived(index, ack.Seq, arrived);
        lock (_ackLock)
            _lastAck = new BotAck(ack.Seq, ack.X, ack.Z, ack.VelX, ack.VelZ);
        _firstAck?.TrySetResult();
    }

    /// <summary>Closes the current connection, if any; its late acks are no longer this bot's.</summary>
    private async Task CloseAsync()
    {
        WorldConnection? connection = _connection;
        if (connection is null) return;

        connection.Ack -= _onAck;
        _onAck = null;
        _connection = null;
        await connection.DisposeAsync();
        metrics.ForgetPending(index);
    }

    /// <summary>
    /// Runs one step under its own timeout, reports its duration, and turns what can go wrong in it into a
    /// <see cref="BotStepException"/> whose kind names the step and the failure. The caller's cancellation stays one.
    /// </summary>
    /// <param name="timeoutReason">What a timeout is reported as; by default, no answer within <paramref name="timeout"/>.</param>
    private async Task<T> StepAsync<T>(string step, TimeSpan timeout, Func<CancellationToken, Task<T>> work,
        CancellationToken ct, string? timeoutReason = null)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        long start = Stopwatch.GetTimestamp();
        try
        {
            T result = await work(limit.Token);
            StepTimed?.Invoke(step, Stopwatch.GetElapsedTime(start));
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new BotStepException(step, $"{step}:timeout", timeoutReason ?? $"no answer within {timeout.TotalSeconds:0} s");
        }
        catch (ApiException error)
        {
            // The API's error code when it is one (ACTIVE_GAME_SESSION, WORLD_UNAVAILABLE), else the status.
            string code = error.Detail.Length is > 0 and <= 40 && error.Detail.All(c => char.IsAsciiLetter(c) || c == '_')
                ? error.Detail
                : error.Status.ToString(System.Globalization.CultureInfo.InvariantCulture);
            throw new BotStepException(step, $"{step}:{code}", error.Message);
        }
        catch (WorldRefusedException error)
        {
            throw new BotStepException(step, $"{step}:{error.Code}", error.Message);
        }
        catch (WorldClosedException error)
        {
            throw new BotStepException(step, $"{step}:closed", error.Message);
        }
        catch (AuthenticationException error)
        {
            throw new BotStepException(step, $"{step}:tls", $"TLS failed (is the certificate the pinned one?): {error.Message}");
        }
        catch (Exception error) when (error is IOException or SocketException or ObjectDisposedException
            or InvalidDataException or CryptographicException or ProtoBuf.ProtoException)
        {
            throw new BotStepException(step, $"{step}:io", error.Message);
        }
    }
}

/// <summary>A step of a bot's way into or out of the world failed.</summary>
/// <param name="step">The step: <c>sign-in</c>, <c>join</c>, <c>connect</c>, <c>admission</c>, <c>handshake</c>,
/// <c>list</c>, <c>create</c>, <c>select</c>, <c>spawn</c>, <c>first-ack</c> or <c>leave</c>.</param>
/// <param name="kind">The step and what went wrong, for the failure counts: <c>join:ACTIVE_GAME_SESSION</c>, <c>spawn:timeout</c>, ...</param>
/// <param name="reason">What went wrong, for a person; never a secret.</param>
public sealed class BotStepException(string step, string kind, string reason) : Exception($"{step}: {reason}")
{
    public string Step { get; } = step;

    public string Kind { get; } = kind;

    public string Reason { get; } = reason;
}
