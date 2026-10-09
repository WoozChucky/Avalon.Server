using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Infrastructure;
using Avalon.Server.Auth.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;

namespace Avalon.Server.Auth.UnitTests.Hosting;

/// <summary>
/// The auth server's port opens only once the server can serve what arrives on it (#867): the
/// certificate loaded, every account reset offline, the account-disconnect subscription live and
/// the connection listener registered. Before, <c>ServerBase.StartAsync</c> listened at host start,
/// so a client could meet a null certificate, or log in before the reset cleared its flag.
/// </summary>
public sealed class AuthServerStartupShould : IDisposable
{
    private const string CertificatePassword = "test";
    private static readonly TimeSpan s_limit = TimeSpan.FromSeconds(5);

    private readonly string _certificatePath = Path.Combine(Path.GetTempPath(), $"avalon-auth-{Guid.NewGuid():N}.pfx");
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();

    public AuthServerStartupShould()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=avalon-test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 certificate =
            request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        File.WriteAllBytes(_certificatePath, certificate.Export(X509ContentType.Pkcs12, CertificatePassword));
        _accounts.ListOnlineSessionsAsync(Arg.Any<CancellationToken>()).Returns([]);
    }

    public void Dispose() => File.Delete(_certificatePath);

    [Fact]
    public async Task Refuse_connections_until_the_server_is_ready()
    {
        // The subscription is the last step before the listener: held, everything before it has run.
        var subscribed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _cache.SubscribeAsync(CacheKeys.WorldAccountsDisconnectChannel, Arg.Any<Action<RedisChannel, RedisValue>>())
            .Returns(subscribed.Task);
        AuthServer server = Create(CertificatePassword);

        await server.StartAsync(CancellationToken.None);
        try
        {
            await UntilAsync(() => Task.FromResult(_cache.ReceivedCalls().Any()), "the server never subscribed");
            Assert.NotNull(server.Certificate);
            await _accounts.Received(1).MarkAllOfflineAsync(Arg.Any<CancellationToken>());
            Assert.False(await AcceptsAsync(server), "a client was accepted before the server was ready");

            subscribed.SetResult();

            await UntilAsync(() => AcceptsAsync(server), "the port did not open once the server was ready");
        }
        finally
        {
            // A held subscription would hold the stop too: it waits for ExecuteAsync.
            subscribed.TrySetResult();
            await server.StopAsync(CancellationToken.None).WaitAsync(s_limit);
        }
    }

    /// <summary>
    /// A stop that begins during the start-up reaches OnStoppingAsync's unsubscribe before the
    /// subscription exists; the start-up's own subscription must still be removed.
    /// </summary>
    [Fact]
    public async Task Unsubscribe_when_the_stop_began_before_the_subscription()
    {
        var reset = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _accounts.MarkAllOfflineAsync(Arg.Any<CancellationToken>()).Returns(reset.Task);
        AuthServer server = Create(CertificatePassword);

        await server.StartAsync(CancellationToken.None);
        await UntilAsync(() => Task.FromResult(_accounts.ReceivedCalls().Any()), "the start-up reset never ran");

        Task stopping = server.StopAsync(CancellationToken.None);
        reset.SetResult();
        await stopping.WaitAsync(s_limit);

        var calls = _cache.ReceivedCalls().Select(c => c.GetMethodInfo().Name).ToList();
        int subscribedAt = calls.IndexOf(nameof(IReplicatedCache.SubscribeAsync));
        Assert.True(subscribedAt >= 0, "the start-up never subscribed");
        Assert.True(calls.LastIndexOf(nameof(IReplicatedCache.UnsubscribeAsync)) > subscribedAt,
            "the subscription made after the stop began was left live");
        Assert.False(await AcceptsAsync(server), "the port opened after the stop began");
    }

    [Fact]
    public async Task Never_open_the_port_when_the_certificate_cannot_be_loaded()
    {
        AuthServer server = Create("not the password");

        await server.StartAsync(CancellationToken.None);
        try
        {
            await Assert.ThrowsAnyAsync<CryptographicException>(() => server.ExecuteTask!.WaitAsync(s_limit));
            Assert.False(await AcceptsAsync(server), "a client was accepted without a certificate");
            await _accounts.DidNotReceive().MarkAllOfflineAsync(Arg.Any<CancellationToken>());
        }
        finally
        {
            await server.StopAsync(CancellationToken.None).WaitAsync(s_limit);
        }
    }

    private AuthServer Create(string certificatePassword) =>
        new(Substitute.For<IServiceProvider>(), Substitute.For<IPacketManager>(), NullLoggerFactory.Instance,
            _accounts, _cache,
            Options.Create(new HostingConfiguration { Port = 0, Host = "127.0.0.1" }),
            Options.Create(new HostingSecurity
            {
                CertificatePath = _certificatePath,
                CertificatePassword = certificatePassword,
            }));

    /// <summary>Whether a client can connect: never while the listener is unbound (port 0 binds a free one, #841).</summary>
    private static async Task<bool> AcceptsAsync(AuthServer server)
    {
        if (server.BoundEndPoint is not { } endPoint)
            return false;

        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, endPoint.Port).WaitAsync(s_limit);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static async Task UntilAsync(Func<Task<bool>> condition, string failure)
    {
        DateTime deadline = DateTime.UtcNow + s_limit;
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, failure);
            await Task.Delay(20);
        }
    }
}
