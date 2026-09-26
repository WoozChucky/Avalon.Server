using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Hosting.Networking;
using Avalon.Infrastructure;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Generic;
using Avalon.Server.Auth.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Avalon.Server.Auth;

public class AuthServer(
    IServiceProvider serviceProvider,
    IPacketManager packetManager,
    ILoggerFactory loggerFactory,
    IAccountRepository accountRepository,
    IReplicatedCache cache,
    IOptions<HostingConfiguration> hostingOptions,
    IOptions<HostingSecurity> securityOptions)
    : ServerBase<AuthConnection>(packetManager, loggerFactory.CreateLogger<AuthServer>(),
        serviceProvider, hostingOptions)
{
    private static readonly MethodInfo s_buildContextMethod =
        typeof(AuthServer).GetMethod(nameof(BuildContextFactory), BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            $"Could not reflect {nameof(AuthServer)}.{nameof(BuildContextFactory)}. " +
            "Ensure the method is non-public, static, and not overloaded.");

    private readonly ConcurrentDictionary<Type, Func<IConnection, Packet?, object>>
        _contextFactoryCache = new();

    private readonly HostingSecurity _securityOptions = securityOptions.Value;

    private readonly ILogger<AuthServer> _logger = loggerFactory.CreateLogger<AuthServer>();

    public new ImmutableArray<IAuthConnection> Connections =>
        TypedConnections.CastArray<IAuthConnection>();

    public X509Certificate2 Certificate { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        byte[] serverCertBytes = await File.ReadAllBytesAsync(_securityOptions.CertificatePath, stoppingToken);

        Certificate = X509CertificateLoader.LoadPkcs12(serverCertBytes, _securityOptions.CertificatePassword);

        // Reset account online status: one statement that writes only the flag and its session
        // (#484, #487). Reading every row and writing each back whole would undo any ban or lock
        // written in between.
        //
        // Exactly one auth server is supported (#487), and this reset is where that is assumed: it
        // clears every account's Online flag, including any a second server's live connections
        // set. The duplicate-login check (ALREADY_CONNECTED, then closing the other connection)
        // looks only at this server's connections too, so a second server would break it with or
        // without this reset; scoping the reset to this server's sessions would fix one half of a
        // setup that does not work anyway. The Helm chart runs one replica.
        await accountRepository.MarkAllOfflineAsync(stoppingToken);

        await SubscribeToAccountDisconnectsAsync();

        RegisterNewConnectionListener(NewConnection);
    }

    protected override async Task OnStoppingAsync(CancellationToken stoppingToken)
    {
        if (_accountDisconnectHandler != null)
        {
            try
            {
                await cache.UnsubscribeAsync(CacheKeys.WorldAccountsDisconnectChannel, _accountDisconnectHandler);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not unsubscribe from the account disconnect channel");
            }
        }

        // Awaited, and all at once: the shutdown notice is delivered by the close, so returning
        // before they finish lets the host exit with the packets still queued.
        var closing = new List<Task>();
        foreach (IAuthConnection connection in Connections)
            closing.Add(GracefulShutdownHelper.NotifyAndCloseAsync(connection, "Server is shutting down", DisconnectReason.ServerShutdown, _logger));

        await Task.WhenAll(closing).ConfigureAwait(false);
    }

    /// <summary>What a connection closed by <see cref="CloseAccountConnections"/> is told.</summary>
    public const string SessionEndedMessage = "Your session has ended. Please log in again.";

    private Action<RedisChannel, RedisValue>? _accountDisconnectHandler;

    /// <summary>
    /// How long this server remembers publishing a duplicate-login disconnect for an account
    /// (#495 review). Pub/sub hands the message back to this server too, typically within
    /// milliseconds; a connection that logged in after the publish, inside this window, is spared.
    /// </summary>
    public static readonly TimeSpan OwnPublishWindow = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<long, long> _ownDisconnectPublishes = new();

    /// <summary>
    /// Records that this server is about to publish a duplicate-login disconnect for
    /// <paramref name="accountId"/> (<c>ALREADY_CONNECTED</c>), so that the login which follows is
    /// not kicked when the message comes back round. The channel's message is the bare account id
    /// the World server parses, so it cannot carry an origin; this is kept here instead.
    /// </summary>
    public void NoteOwnDisconnectPublish(Avalon.Common.ValueObjects.AccountId accountId) =>
        NoteOwnDisconnectPublish(accountId, System.Diagnostics.Stopwatch.GetTimestamp());

    /// <inheritdoc cref="NoteOwnDisconnectPublish(Avalon.Common.ValueObjects.AccountId)"/>
    public void NoteOwnDisconnectPublish(Avalon.Common.ValueObjects.AccountId accountId, long now)
    {
        // Notes whose echo never came (a lost message, a Redis reconnect) go on the next write, so
        // the map holds at most the publishes of the last window (#495 re-review).
        long window = OwnPublishWindowTicks;
        foreach (KeyValuePair<long, long> note in _ownDisconnectPublishes)
        {
            if (now - note.Value > window)
                _ownDisconnectPublishes.TryRemove(note);
        }

        _ownDisconnectPublishes[accountId.Value] = now;
    }

    private static long OwnPublishWindowTicks =>
        (long)(OwnPublishWindow.TotalSeconds * System.Diagnostics.Stopwatch.Frequency);

    /// <summary>How many own-publish notes are held.</summary>
    public int OwnPublishNoteCount => _ownDisconnectPublishes.Count;

    /// <summary>
    /// Handles one message on the account disconnect channel at <paramref name="now"/>: closes the
    /// account's connections, sparing those that logged in after this server's own note for it.
    /// <para>
    /// The note is not consumed by the message (#495 final review). The message is a bare account
    /// id, so a ban and this server's own echo cannot be told apart, and a note dropped on the first
    /// one let the second kick the retry. It lasts its window instead, then expires (dropped on the
    /// next read or write). Sparing the retry from a ban inside the window costs nothing: a ban
    /// delivered before the echo was published before this server's publish, so before the retry's
    /// login, which read the banned row and was refused; and one delivered after it still meets the
    /// post-login guard, which closes a banned session on its next request.
    /// </para>
    /// </summary>
    public int HandleAccountDisconnect(RedisValue message, long now) =>
        HandleAccountDisconnect(Connections, message, now);

    /// <inheritdoc cref="HandleAccountDisconnect(RedisValue, long)"/>
    public int HandleAccountDisconnect(IEnumerable<IAuthConnection> connections, RedisValue message, long now)
    {
        if (TryParseAccount(message) is not { } account)
            return CloseAccountConnections(connections, message, _logger);

        long? noted = OwnDisconnectPublishedAt(account, now);
        return CloseAccountConnections(connections, message, _logger, _ => noted);
    }

    private static Avalon.Common.ValueObjects.AccountId? TryParseAccount(RedisValue message) =>
        long.TryParse(message.ToString(), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out long id)
            ? new Avalon.Common.ValueObjects.AccountId(id)
            : null;

    /// <summary>
    /// When this server last noted its own disconnect publish for <paramref name="accountId"/>, if
    /// within <see cref="OwnPublishWindow"/> of <paramref name="now"/> (Stopwatch timestamps);
    /// otherwise null, and a note that old is dropped.
    /// </summary>
    public long? OwnDisconnectPublishedAt(Avalon.Common.ValueObjects.AccountId accountId, long now)
    {
        if (!_ownDisconnectPublishes.TryGetValue(accountId.Value, out long at))
            return null;
        if (now - at <= (long)(OwnPublishWindow.TotalSeconds * System.Diagnostics.Stopwatch.Frequency))
            return at;

        _ownDisconnectPublishes.TryRemove(new KeyValuePair<long, long>(accountId.Value, at));
        return null;
    }

    /// <summary>
    /// Listens on <see cref="CacheKeys.WorldAccountsDisconnectChannel"/> (#495). Everything that
    /// ends an account's sessions publishes there (a password change, an MFA reset or removal, a
    /// ban, a refresh-token reuse, a duplicate login), and a logged-in connection here is a session
    /// too: left open, it could go on asking for world keys with the old credentials.
    /// </summary>
    public Task SubscribeToAccountDisconnectsAsync()
    {
        _accountDisconnectHandler ??= (_, message) =>
            HandleAccountDisconnect(message, System.Diagnostics.Stopwatch.GetTimestamp());
        return cache.SubscribeAsync(CacheKeys.WorldAccountsDisconnectChannel, _accountDisconnectHandler);
    }

    /// <summary>
    /// Closes every connection in <paramref name="connections"/> logged in as the account
    /// <paramref name="message"/> names, telling it why first. A message that names no account is
    /// ignored. Returns how many were closed. A connection not logged in yet is left alone: the
    /// account it may log in to next is checked then, against its current credentials.
    /// <para>
    /// When <paramref name="ownPublishAt"/> says this server published a duplicate-login disconnect
    /// for the account at some instant, a connection that logged in after it is spared (#495
    /// review): the message may be that very publish, made by its own first login attempt. The cost
    /// is that a different publish for the account inside the window also spares it; world select
    /// still checks the credentials version and the status, so such a connection gets no world key
    /// for changed credentials or a banned account.
    /// </para>
    /// <para>One connection that throws while closing is logged and does not stop the others.</para>
    /// </summary>
    public static int CloseAccountConnections(IEnumerable<IAuthConnection> connections, RedisValue message,
        ILogger logger, Func<Avalon.Common.ValueObjects.AccountId, long?>? ownPublishAt = null)
    {
        if (!long.TryParse(message.ToString(), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out long id))
        {
            logger.LogWarning("Ignored an account disconnect that names no account: {Message}", message.ToString());
            return 0;
        }

        var accountId = new Avalon.Common.ValueObjects.AccountId(id);
        int closed = 0;
        foreach (IAuthConnection connection in connections)
        {
            if (connection.AccountId != accountId)
                continue;

            if (ownPublishAt?.Invoke(accountId) is { } publishedAt && connection.LoggedInAt > publishedAt)
            {
                logger.LogInformation(
                    "Spared auth connection {EndPoint} of account {AccountId}: it logged in after this server's own disconnect publish",
                    connection.RemoteEndPoint, id);
                continue;
            }

            try
            {
                logger.LogInformation("Closing auth connection {EndPoint} of account {AccountId}: its sessions were ended",
                    connection.RemoteEndPoint, id);
                GracefulShutdownHelper.NotifyAndClose(connection, SessionEndedMessage, DisconnectReason.Kicked, logger);
                closed++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not close auth connection {EndPoint} of account {AccountId}",
                    connection.RemoteEndPoint, id);
            }
        }

        return closed;
    }

    private bool NewConnection(IConnection connection) => true;

    protected override object GetContextPacket(IConnection connection, object? packet, Type packetType)
    {
        var factory = _contextFactoryCache.GetOrAdd(packetType, static t =>
            (Func<IConnection, Packet?, object>)s_buildContextMethod.MakeGenericMethod(t).Invoke(null, null)!);
        return factory(connection, packet as Packet);
    }

    private static Func<IConnection, Packet?, object> BuildContextFactory<TPacket>() where TPacket : Packet
        => static (conn, pkt) => new AuthPacketContext<TPacket>
            { Connection = (IAuthConnection)conn!, Packet = (TPacket)pkt! };
}
