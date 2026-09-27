using System.Net;
using System.Net.Sockets;
using Avalon.Common.Cryptography;
using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Avalon.Server.Auth.UnitTests.Networking;

/// <summary>
/// #571: every socket the shared TCP server accepts has TCP keepalive on, with the configured idle
/// time, probe interval and probe count, so the OS closes a half-open connection (the peer gone
/// without a FIN or RST) and the close path runs. A real loopback listener accepts a real client.
/// </summary>
public class AcceptedSocketKeepAliveShould
{
    /// <summary>Hands the accepted client to the test; ServerBase builds it with (TcpClient, IServerBase).</summary>
    private sealed class Accepted
    {
        public TaskCompletionSource<TcpClient> Client { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class CapturingConnection : BackgroundService, IConnection
    {
        private readonly TcpClient _client;
        private readonly Accepted _accepted;

        // ReSharper disable once UnusedParameter.Local — required by ActivatorUtilities
        public CapturingConnection(TcpClient client, IServerBase _, Accepted accepted)
        {
            Id = Guid.NewGuid();
            _client = client;
            _accepted = accepted;
        }

        public Guid Id { get; }
        public new Task? ExecuteTask => Task.CompletedTask;
        public string RemoteEndPoint => "capture";
        public IAvalonCryptoSession CryptoSession => null!;
        public ICryptoManager ServerCrypto => null!;
        public void Close(bool expected = true) { }
        public Task CloseAsync(bool expected = true) => Task.CompletedTask;
        public void Send(NetworkPacket packet) { }
        // Handed over only once ServerBase has started it, after it has gone back to accepting, so
        // the test's StopAsync never lands between the accept and that re-arm.
        public new Task StartAsync(CancellationToken token = default)
        {
            _accepted.Client.TrySetResult(_client);
            return Task.CompletedTask;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }

    private sealed class TestServer : ServerBase<CapturingConnection>
    {
        public TestServer(IServiceProvider services, IOptions<HostingConfiguration> options)
            : base(Substitute.For<IPacketManager>(), NullLogger.Instance, services, options) { }

        protected override object GetContextPacket(IConnection connection, object? packet, Type packetType) => null!;
        protected override Task OnStoppingAsync(CancellationToken stoppingToken) => Task.CompletedTask;
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Delay(Timeout.Infinite, stoppingToken);
    }

    [Fact]
    public async Task Turn_on_keepalive_with_the_configured_values()
    {
        using TcpClient accepted = await AcceptOneAsync(new HostingConfiguration
        {
            TcpKeepAliveTimeSeconds = 45,
            TcpKeepAliveIntervalSeconds = 7,
            TcpKeepAliveRetryCount = 4,
        });

        AssertKeepAlive(accepted.Client, time: 45, interval: 7, retries: 4);
    }

    [Fact]
    public async Task Turn_on_keepalive_with_the_defaults_when_nothing_is_configured()
    {
        using TcpClient accepted = await AcceptOneAsync(new HostingConfiguration());

        AssertKeepAlive(accepted.Client, time: 60, interval: 10, retries: 3);
    }

    [Fact]
    public void Warn_once_and_throw_nothing_when_the_platform_refuses_an_option()
    {
        // A UDP socket refuses the TCP-level keepalive options, as a platform without them would.
        var logger = new CapturingLogger();
        var keepAlive = new TcpKeepAlive(new HostingConfiguration(), logger);
        using var udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        keepAlive.Apply(udp);
        keepAlive.Apply(udp);

        Assert.Equal(1, logger.Count(LogLevel.Warning));
    }

    [Theory]
    [InlineData(SocketError.ProtocolOption, true)]
    [InlineData(SocketError.OperationNotSupported, true)]
    [InlineData(SocketError.InvalidArgument, true)]
    [InlineData(SocketError.ConnectionReset, false)]
    [InlineData(SocketError.NotConnected, false)]
    public void Count_only_an_unsupported_option_as_a_platform_refusal(SocketError error, bool unsupported)
    {
        Assert.Equal(unsupported, TcpKeepAlive.IsUnsupportedOption(new SocketException((int)error)));
    }

    private static void AssertKeepAlive(Socket socket, int time, int interval, int retries)
    {
        Assert.NotEqual(0, (int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive)!);
        // Windows (10 1709 and later), Linux and macOS all report these three; only a platform that
        // cannot read one back (ReadTcpOption returns null) leaves that one option unchecked.
        AssertOption(socket, SocketOptionName.TcpKeepAliveTime, time);
        AssertOption(socket, SocketOptionName.TcpKeepAliveInterval, interval);
        AssertOption(socket, SocketOptionName.TcpKeepAliveRetryCount, retries);
    }

    private static void AssertOption(Socket socket, SocketOptionName option, int expected)
    {
        int? actual = ReadTcpOption(socket, option);
        if (actual is not null)
            Assert.Equal(expected, actual.Value);
    }

    private static int? ReadTcpOption(Socket socket, SocketOptionName option)
    {
        try
        {
            return (int)socket.GetSocketOption(SocketOptionLevel.Tcp, option)!;
        }
        catch (Exception e) when (e is SocketException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    private static async Task<TcpClient> AcceptOneAsync(HostingConfiguration configuration)
    {
        configuration.Host = "127.0.0.1";
        configuration.Port = GetFreePort();

        var accepted = new Accepted();
        await using ServiceProvider services = new ServiceCollection().AddSingleton(accepted).BuildServiceProvider();
        var server = new TestServer(services, Options.Create(configuration));
        await server.StartAsync(CancellationToken.None);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, configuration.Port);
            return await accepted.Client.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    private static ushort GetFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return (ushort)port;
    }
}
