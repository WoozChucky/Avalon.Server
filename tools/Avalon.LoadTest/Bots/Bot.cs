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
/// input driver) while the bot is <see cref="BotState.InWorld"/>.
/// </summary>
public sealed class Bot(int index, string account, string password, ApiClient api, ushort worldId, string? dialHost,
    BotMetrics metrics)
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

    /// <summary>Above the world's 15 s readiness barrier, which spawns a character whose load report was lost.</summary>
    private static readonly TimeSpan s_spawnTimeout = TimeSpan.FromSeconds(30);

    /// <summary>A leave waits for the character's logout save.</summary>
    private static readonly TimeSpan s_leaveTimeout = TimeSpan.FromSeconds(20);

    /// <summary>How often a bot waiting for its spawn sends an idle input; the first one answered means it is in.</summary>
    private static readonly TimeSpan s_spawnProbeInterval = TimeSpan.FromMilliseconds(50);

    private readonly Lock _ackLock = new();
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

    public BotState State => _state;

    /// <summary>The bot's game context once signed in, which its refresher keeps alive; null when signed out.</summary>
    public GameContext? Context { get; private set; }

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
    /// Enters the world: join ticket, connection, admission, handshake, list, create when the character is missing,
    /// select, load report, and the first input answered. A failed attempt is counted by kind and retried up to three
    /// times after 1 s, 3 s and 9 s, the retries taking over any session the failed attempt left; the last failure is
    /// thrown as a <see cref="BotStepException"/>. <paramref name="takeover"/> lets the first attempt replace the
    /// account's live world session.
    /// </summary>
    public async Task EnterAsync(bool takeover, CancellationToken ct)
    {
        if (Context is null) throw new InvalidOperationException($"Bot {index} is not signed in.");

        for (int attempt = 0; ; attempt++)
        {
            metrics.EntryAttempt();
            try
            {
                await EnterOnceAsync(takeover || attempt > 0, ct);
                return;
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
            // A leave is answered only once the handshake is done; before it the world ignores it.
            if (was is BotState.Selecting or BotState.Loaded or BotState.InWorld && !connection.Closed.IsCompleted)
            {
                try
                {
                    await LeaveCharacterAsync(connection, ct);
                }
                catch (BotStepException error)
                {
                    Note?.Invoke($"Leave: {error.Reason}; closing the connection anyway.");
                }
            }

            await CloseAsync();
        }

        _state = Context is null ? BotState.SignedOut : BotState.SignedIn;
    }

    /// <summary>Leaves the world (<see cref="DisconnectAsync"/>) and signs the game context out.</summary>
    public async Task LeaveAsync(CancellationToken ct)
    {
        await DisconnectAsync(ct);
        if (Context is { } context)
        {
            long start = Stopwatch.GetTimestamp();
            try
            {
                await api.LogoutAsync(context, ct);
            }
            catch (ApiException error)
            {
                Note?.Invoke($"Logout: {error.Message}.");
            }

            Context = null;
            StepTimed?.Invoke("logout", Stopwatch.GetElapsedTime(start));
        }

        _state = BotState.Stopped;
    }

    /// <summary>The next input number on the current connection; they restart at 1 on each connection.</summary>
    public uint NextSeq() => Interlocked.Increment(ref _seq);

    /// <summary>
    /// A sealed <c>CMSG_PLAYER_INPUT</c> for the current connection, its send time noted for the ack latency: send it
    /// at once with <see cref="SendAsync"/>.
    /// </summary>
    public NetworkPacket NextInput(uint seq, float dirX, float dirZ, ushort yaw)
    {
        WorldConnection connection = _connection ?? throw new InvalidOperationException($"Bot {index} has no connection.");
        NetworkPacket packet = connection.Seal(
            new CPlayerInputPacket { Seq = seq, DirX = dirX, DirZ = dirZ, YawDeg = yaw }, NetworkPacketType.CMSG_PLAYER_INPUT);
        metrics.InputSent(index, seq, Stopwatch.GetTimestamp());
        return packet;
    }

    /// <summary>Sends a packet on the current connection.</summary>
    public ValueTask SendAsync(NetworkPacket packet, CancellationToken ct) =>
        (_connection ?? throw new InvalidOperationException($"Bot {index} has no connection.")).SendAsync(packet, ct);

    private async Task EnterOnceAsync(bool takeover, CancellationToken ct)
    {
        GameContext context = Context!;
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
                await connection.SendAsync(connection.Seal(
                    new CCharacterCreatePacket { Name = account, Class = index % 4 + 1, Gender = index % 2 },
                    NetworkPacketType.CMSG_CHARACTER_CREATE), token);
                NetworkPacket reply = await connection.ExpectAsync(NetworkPacketType.SMSG_CHARACTER_CREATED, token);
                return connection.Codec.Decode<SCharacterCreatedPacket>(reply).Result;
            }, ct);

            // NameAlreadyExists: an earlier attempt made it and its answer was lost; the list shows it.
            if (result is not (SCharacterCreateResult.Success or SCharacterCreateResult.NameAlreadyExists))
                throw new BotStepException("create", $"create:{result}", $"the world refused to create {account}: {result}");

            character = await ListAsync(connection, ct)
                ?? throw new BotStepException("create", $"create:{result}", $"{account} is not on the character list after a create answered {result}");
        }

        uint characterId = character.CharacterId;
        await StepAsync("select", s_characterTimeout, async token =>
        {
            await connection.SendAsync(connection.Seal(new CCharacterSelectedPacket { CharacterId = characterId },
                NetworkPacketType.CMSG_CHARACTER_SELECTED), token);
            await connection.ExpectAsync(NetworkPacketType.SMSG_CHARACTER_SELECTED, token);
            // At once: the world holds the spawn until the client reports it has loaded (15 s otherwise).
            await connection.SendAsync(connection.Seal(new CCharacterLoadedPacket(),
                NetworkPacketType.CMSG_CHARACTER_LOADED), token);
            return true;
        }, ct);
        _state = BotState.Loaded;

        await StepAsync("spawn", s_spawnTimeout, async token =>
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
            await connection.SendAsync(connection.Seal(new CCharacterListPacket(), NetworkPacketType.CMSG_CHARACTER_LIST), token);
            NetworkPacket reply = await connection.ExpectAsync(NetworkPacketType.SMSG_CHARACTER_LIST, token);
            CharacterInfo[]? characters = connection.Codec.Decode<SCharacterListPacket>(reply).Characters;
            return characters?.FirstOrDefault(c => string.Equals(c.Name, account, StringComparison.OrdinalIgnoreCase));
        }, ct);

    /// <summary>
    /// Idle inputs until one is answered: the world drops input from a connection whose character has not spawned yet,
    /// and answers every one after, so the first ack is the moment the character is in the world.
    /// </summary>
    private async Task WaitForFirstAckAsync(WorldConnection connection, CancellationToken ct)
    {
        var firstAck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _firstAck = firstAck;
        try
        {
            while (!firstAck.Task.IsCompleted)
            {
                // The probes the world dropped are never answered: none is held long enough to count as latency.
                metrics.ForgetPending(index);
                await connection.SendAsync(NextInput(NextSeq(), 0f, 0f, 0), ct);
                Task closed = connection.Closed;
                Task first = await Task.WhenAny(firstAck.Task, closed, Task.Delay(s_spawnProbeInterval, ct));
                if (first == closed) throw new WorldClosedException("the connection closed before the character spawned");
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
            await connection.SendAsync(connection.Seal(new CCharacterLeavePacket(), NetworkPacketType.CMSG_CHARACTER_LEAVE), token);
            NetworkPacket reply = await connection.ExpectAsync(NetworkPacketType.SMSG_CHARACTER_LEAVE_RESULT, token);
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
    private async Task<T> StepAsync<T>(string step, TimeSpan timeout, Func<CancellationToken, Task<T>> work,
        CancellationToken ct)
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
            throw new BotStepException(step, $"{step}:timeout", $"no answer within {timeout.TotalSeconds:0} s");
        }
        catch (ApiException error)
        {
            // The API's error code when it is one word (ActiveGameSession, WorldUnavailable), else the status.
            string code = error.Detail.Length is > 0 and <= 40 && error.Detail.All(char.IsAsciiLetter)
                ? error.Detail
                : error.Status.ToString(System.Globalization.CultureInfo.InvariantCulture);
            throw new BotStepException(step, $"{step}:{code}", error.Message);
        }
        catch (AdmissionRefusedException error)
        {
            throw new BotStepException(step, $"{step}:{error.Result}", error.Message);
        }
        catch (WorldRefusedException error)
        {
            throw new BotStepException(step, $"{step}:refused", error.Message);
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
/// <c>list</c>, <c>create</c>, <c>select</c>, <c>spawn</c> or <c>leave</c>.</param>
/// <param name="kind">The step and what went wrong, for the failure counts: <c>join:ActiveGameSession</c>, <c>spawn:timeout</c>, ...</param>
/// <param name="reason">What went wrong, for a person; never a secret.</param>
public sealed class BotStepException(string step, string kind, string reason) : Exception($"{step}: {reason}")
{
    public string Step { get; } = step;

    public string Kind { get; } = kind;

    public string Reason { get; } = reason;
}
