using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Avalon.Common.Cryptography;
using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Avalon.Server.Auth.UnitTests.Networking;

/// <summary>
/// Integration-level tests for ServerBase shutdown behaviour.
/// These use real TCP sockets on ephemeral ports — no external services needed.
/// </summary>
public class ServerBaseShould
{
    // ── Minimal test doubles ────────────────────────────────────────────────

    /// <summary>
    /// A no-op IConnection that satisfies the ServerBase generic constraint.
    /// ActivatorUtilities creates it with (TcpClient, IServerBase) based on the
    /// accept loop's factory call in ServerBase.
    /// Because the test never connects a real client these fields are never used.
    /// </summary>
    private sealed class StubConnection : BackgroundService, IConnection
    {
        // ReSharper disable once UnusedParameter.Local — required by ActivatorUtilities
        public StubConnection(System.Net.Sockets.TcpClient _, IServerBase __)
        {
            Id = Guid.NewGuid();
        }

        public Guid Id { get; }
        public new Task? ExecuteTask => ExecuteAsync(CancellationToken.None);
        public string RemoteEndPoint => "stub";
        public IAvalonCryptoSession CryptoSession => null!;
        public ICryptoManager ServerCrypto => null!;
        public void Close(bool expected = true) { }
        public Task CloseAsync(bool expected = true) => Task.CompletedTask;
        public void Send(NetworkPacket packet) { }
        public new Task StartAsync(CancellationToken token = default) => Task.CompletedTask;
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }

    private sealed class TestServerBase : ServerBase<StubConnection>
    {
        public TestServerBase(IPacketManager packetManager, IOptions<HostingConfiguration> opts)
            : base(packetManager, NullLogger.Instance, Substitute.For<IServiceProvider>(), opts) { }

        protected override object GetContextPacket(IConnection connection, object? packet, Type packetType)
            => null!;

        protected override Task OnStoppingAsync(CancellationToken stoppingToken)
            => Task.CompletedTask;

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
            => Task.Delay(Timeout.Infinite, stoppingToken);
    }

    // ── Helper ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Asks the OS for a free ephemeral port so tests never conflict.
    /// </summary>
    private static ushort GetFreePort()
    {
        var tmp = new TcpListener(IPAddress.Loopback, 0);
        tmp.Start();
        int port = ((IPEndPoint)tmp.LocalEndpoint).Port;
        tmp.Stop();
        return (ushort)port;
    }

    private static TestServerBase CreateServer(ushort port)
    {
        var opts = Options.Create(new HostingConfiguration { Host = "127.0.0.1", Port = port });
        return new TestServerBase(Substitute.For<IPacketManager>(), opts);
    }

    // ── Tests ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The key regression test: after StopAsync the OS port must be free.
    /// Before the fix, Listener.Stop() was never called, leaving the socket
    /// bound and any attempt to re-bind the same port would throw.
    /// </summary>
    [Fact]
    public async Task ReleaseListeningPort_AfterStopAsync()
    {
        ushort port = GetFreePort();
        var server = CreateServer(port);

        await server.StartAsync(CancellationToken.None);
        await server.StopAsync(CancellationToken.None);

        // If the listener socket was properly closed, we can bind the same port again.
        var probe = new TcpListener(IPAddress.Loopback, port);
        probe.Start(); // Throws SocketException if port is still held.
        probe.Stop();
    }

    /// <summary>
    /// StopAsync must complete without throwing even when there are no active connections.
    /// </summary>
    [Fact]
    public async Task CompleteWithoutException_WhenStoppedWithNoConnections()
    {
        ushort port = GetFreePort();
        var server = CreateServer(port);

        await server.StartAsync(CancellationToken.None);

        var exception = await Record.ExceptionAsync(() => server.StopAsync(CancellationToken.None));

        Assert.Null(exception);
    }

    // ── Accept loop against a stop (#578) ───────────────────────────────────

    /// <summary>What the accepting connections share with the test; resolved from DI by ActivatorUtilities.</summary>
    private sealed class AcceptProbe
    {
        private int _constructed;
        private int _started;

        /// <summary>When set, each connection's constructor waits here: the accept is done, the serving is not.</summary>
        public ManualResetEventSlim? Gate { get; init; }

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Constructed => Volatile.Read(ref _constructed);
        public int Started => Volatile.Read(ref _started);

        public void OnConstructed()
        {
            Interlocked.Increment(ref _constructed);
            Entered.TrySetResult();
            Gate?.Wait(TimeSpan.FromSeconds(10));
        }

        public void OnStarted() => Interlocked.Increment(ref _started);
    }

    private sealed class ProbeConnection : BackgroundService, IConnection
    {
        private readonly AcceptProbe _probe;

        // ReSharper disable once UnusedParameter.Local — required by ActivatorUtilities
        public ProbeConnection(TcpClient client, IServerBase _, AcceptProbe probe)
        {
            Id = Guid.NewGuid();
            _probe = probe;
            probe.OnConstructed();
        }

        public Guid Id { get; }
        public new Task? ExecuteTask => Task.CompletedTask;
        public string RemoteEndPoint => "probe";
        public IAvalonCryptoSession CryptoSession => null!;
        public ICryptoManager ServerCrypto => null!;
        public void Close(bool expected = true) { }
        public Task CloseAsync(bool expected = true) => Task.CompletedTask;
        public void Send(NetworkPacket packet) { }

        public new Task StartAsync(CancellationToken token = default)
        {
            _probe.OnStarted();
            return Task.CompletedTask;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }

    private sealed class ProbeServer : ServerBase<ProbeConnection>
    {
        public ProbeServer(IServiceProvider services, IOptions<HostingConfiguration> opts)
            : base(Substitute.For<IPacketManager>(), NullLogger.Instance, services, opts) { }

        public int ConnectionCount => Connections.Count;

        /// <summary>What the world server calls from ExecuteAsync, which a stop can overtake.</summary>
        public void Listen() => StartListening();

        protected override object GetContextPacket(IConnection connection, object? packet, Type packetType) => null!;
        protected override Task OnStoppingAsync(CancellationToken stoppingToken) => Task.CompletedTask;
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Delay(Timeout.Infinite, stoppingToken);
    }

    /// <summary>
    /// Records every exception that escapes to the process or goes unobserved while it is alive, keeping
    /// only those thrown from the accept path, so an unrelated test running alongside cannot fail these.
    /// </summary>
    private sealed class EscapeWatchdog : IDisposable
    {
        private readonly ConcurrentQueue<Exception> _escaped = new();

        public EscapeWatchdog()
        {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
            TaskScheduler.UnobservedTaskException += OnUnobserved;
        }

        public IReadOnlyCollection<Exception> Escaped
        {
            get
            {
                // An unobserved task is reported only once it is finalized.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                return _escaped.Where(FromAcceptPath).ToArray();
            }
        }

        private static bool FromAcceptPath(Exception e) =>
            e.ToString().Contains("Avalon.Hosting.Networking.ServerBase", StringComparison.Ordinal);

        private void OnUnhandled(object? sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception exception)
                _escaped.Enqueue(exception);
        }

        private void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e) => _escaped.Enqueue(e.Exception);

        public void Dispose()
        {
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    private static (ProbeServer Server, ServiceProvider Services, ushort Port) CreateProbeServer(AcceptProbe probe)
    {
        ushort port = GetFreePort();
        ServiceProvider services = new ServiceCollection().AddSingleton(probe).BuildServiceProvider();
        var opts = Options.Create(new HostingConfiguration { Host = "127.0.0.1", Port = port });
        return (new ProbeServer(services, opts), services, port);
    }

    /// <summary>
    /// #578: a stop that lands after a client was accepted but before the loop went back to accepting
    /// used to make BeginAcceptTcpClient throw out of an async void, which ends the process.
    /// </summary>
    [Fact]
    public async Task Throw_nothing_when_stopped_while_a_client_is_mid_accept()
    {
        using var watchdog = new EscapeWatchdog();
        using var gate = new ManualResetEventSlim(false);
        var probe = new AcceptProbe { Gate = gate };
        (ProbeServer server, ServiceProvider services, ushort port) = CreateProbeServer(probe);
        await using ServiceProvider _ = services;

        await server.StartAsync(CancellationToken.None);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // The stop begins while the accepted client is still being set up, and the setup then finishes.
        Task stopping = server.StopAsync(CancellationToken.None);
        await Task.WhenAny(stopping, Task.Delay(TimeSpan.FromMilliseconds(200)));
        gate.Set();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        Assert.Empty(watchdog.Escaped);
    }

    [Fact]
    public async Task Survive_a_hundred_connect_and_stop_cycles()
    {
        using var watchdog = new EscapeWatchdog();

        for (int cycle = 0; cycle < 100; cycle++)
        {
            var probe = new AcceptProbe();
            (ProbeServer server, ServiceProvider services, ushort port) = CreateProbeServer(probe);
            await using ServiceProvider _ = services;

            await server.StartAsync(CancellationToken.None);
            using var client = new TcpClient();
            // The stop races the accept: it lands before, during or after it.
            Task connecting = client.ConnectAsync(IPAddress.Loopback, port);
            await server.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            await Record.ExceptionAsync(() => connecting);
        }

        await Task.Delay(TimeSpan.FromMilliseconds(100));
        Assert.Empty(watchdog.Escaped);
    }

    [Fact]
    public async Task Keep_accepting_one_client_after_another()
    {
        var probe = new AcceptProbe();
        (ProbeServer server, ServiceProvider services, ushort port) = CreateProbeServer(probe);
        await using ServiceProvider _ = services;
        await server.StartAsync(CancellationToken.None);

        var clients = new List<TcpClient>();
        try
        {
            for (int i = 1; i <= 3; i++)
            {
                var client = new TcpClient();
                clients.Add(client);
                await client.ConnectAsync(IPAddress.Loopback, port);
                int expected = i;
                await WaitUntilAsync(() => probe.Started == expected);
            }

            Assert.Equal(3, probe.Constructed);
            // Each is registered before it starts, which the auth server's liveness sweep (#555) relies on.
            Assert.Equal(3, server.ConnectionCount);
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
            foreach (TcpClient client in clients)
                client.Dispose();
        }
    }

    [Fact]
    public async Task Accept_nothing_once_stopped_even_when_asked_to_listen_again()
    {
        var probe = new AcceptProbe();
        (ProbeServer server, ServiceProvider services, ushort port) = CreateProbeServer(probe);
        await using ServiceProvider _ = services;
        await server.StartAsync(CancellationToken.None);
        await server.StopAsync(CancellationToken.None);

        // The world server calls StartListening from ExecuteAsync, which a stop can overtake.
        server.Listen();

        using var client = new TcpClient();
        await Assert.ThrowsAsync<SocketException>(() => client.ConnectAsync(IPAddress.Loopback, port));
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        Assert.Equal(0, probe.Constructed);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The condition was not met in time.");
            await Task.Delay(10);
        }
    }
}
