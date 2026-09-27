using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Avalon.Common.Cryptography;
using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Avalon.Hosting.Telemetry;
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

        /// <summary>When set, each connection's StartAsync throws.</summary>
        public bool ThrowOnStart { get; init; }

        /// <summary>The peer port of every client a connection was built for.</summary>
        public ConcurrentBag<int> PeerPorts { get; } = new();

        public void OnConstructed(TcpClient client)
        {
            try
            {
                if (client.Client.RemoteEndPoint is IPEndPoint peer)
                    PeerPorts.Add(peer.Port);
            }
            catch (SocketException)
            {
                // A peer that reset before this point has no address to record.
            }

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
            probe.OnConstructed(client);
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
            if (_probe.ThrowOnStart)
                throw new InvalidOperationException("The connection could not start.");
            return Task.CompletedTask;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }

    private sealed class ProbeServer : ServerBase<ProbeConnection>
    {
        public ProbeServer(IServiceProvider services, IOptions<HostingConfiguration> opts,
            IPacketManager? packets = null, Microsoft.Extensions.Logging.ILogger? logger = null,
            PacketDispatchTelemetry? telemetry = null)
            : base(packets ?? Substitute.For<IPacketManager>(), logger ?? NullLogger.Instance, services, opts, telemetry) { }

        public int ConnectionCount => Connections.Count;

        /// <summary>Thrown, in order, by the next accepts instead of accepting.</summary>
        public ConcurrentQueue<Exception> AcceptFailures { get; } = new();

        /// <summary>Every backoff the loop asked for.</summary>
        public ConcurrentQueue<TimeSpan> Delays { get; } = new();

        /// <summary>When set, a backoff really waits (cancellably); otherwise it returns at once.</summary>
        public bool RealDelays { get; init; }

        public TaskCompletionSource DelayRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override ValueTask<TcpClient> AcceptClientAsync(CancellationToken token) =>
            AcceptFailures.TryDequeue(out Exception? failure)
                ? ValueTask.FromException<TcpClient>(failure)
                : base.AcceptClientAsync(token);

        protected override Task DelayAcceptAsync(TimeSpan delay, CancellationToken token)
        {
            Delays.Enqueue(delay);
            DelayRequested.TrySetResult();
            return RealDelays ? base.DelayAcceptAsync(delay, token) : Task.CompletedTask;
        }

        /// <summary>A late StartListening, such as one from a derived server's ExecuteAsync, which a stop can overtake.</summary>
        public void Listen() => StartListening();

        protected override object GetContextPacket(IConnection connection, object? packet, Type packetType) => null!;
        /// <summary>Runs inside OnStoppingAsync, where a shutdown notifies and closes its connections.</summary>
        public Func<Task>? OnStopping { get; set; }

        protected override Task OnStoppingAsync(CancellationToken stoppingToken) =>
            OnStopping?.Invoke() ?? Task.CompletedTask;

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

    private static (ProbeServer Server, ServiceProvider Services, ushort Port) CreateProbeServer(AcceptProbe probe,
        Microsoft.Extensions.Logging.ILogger? logger = null, PacketDispatchTelemetry? telemetry = null,
        bool realDelays = false)
    {
        ushort port = GetFreePort();
        ServiceProvider services = new ServiceCollection().AddSingleton(probe).BuildServiceProvider();
        var opts = Options.Create(new HostingConfiguration { Host = "127.0.0.1", Port = port });
        return (new ProbeServer(services, opts, logger: logger, telemetry: telemetry) { RealDelays = realDelays },
            services, port);
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

        // A late StartListening (say from a derived ExecuteAsync) must not reopen the port.
        server.Listen();

        using var client = new TcpClient();
        await Assert.ThrowsAsync<SocketException>(() => client.ConnectAsync(IPAddress.Loopback, port));
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        Assert.Equal(0, probe.Constructed);
    }

    // ── Packets against a stop (#578) ───────────────────────────────────────

    /// <summary>What the handler saw of the token ServerBase handed it.</summary>
    private sealed class PacketProbe
    {
        public ManualResetEventSlim? Gate { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<bool> TokensCancelled { get; } = new();
        private int _runs;
        public int Runs => Volatile.Read(ref _runs);
        public void OnRun() => Interlocked.Increment(ref _runs);
    }

    private sealed class TokenProbeHandler(PacketProbe probe) : IPacketHandlerNew
    {
        public Task ExecuteAsync(object context, CancellationToken token)
        {
            probe.OnRun();
            probe.Entered.TrySetResult();
            probe.Gate?.Wait(TimeSpan.FromSeconds(10));
            // A handler registers on the token, as a cancellable await does.
            using CancellationTokenRegistration _ = token.Register(static () => { });
            probe.TokensCancelled.Enqueue(token.IsCancellationRequested);
            // A cancellable await ends with this once the stop cancels its token.
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// #578 review: a packet dispatched after the stop began used to read the stopping token from a
    /// disposed source; the ObjectDisposedException was logged as a handler failure and closed the
    /// connection. It is now dropped, and the shutdown closes the connection. One already running when
    /// the stop begins still finishes, and sees the stop on its token.
    /// </summary>
    [Fact]
    public async Task Drop_a_packet_that_arrives_after_the_stop_began()
    {
        using var watchdog = new EscapeWatchdog();
        using var gate = new ManualResetEventSlim(false);
        var packetProbe = new PacketProbe { Gate = gate };
        var packets = Substitute.For<IPacketManager>();
        packets.TryGetPacketInfo(Arg.Any<NetworkPacketType>(), out Arg.Any<PacketInfo>())
            .Returns(call =>
            {
                call[1] = new PacketInfo(typeof(Packet), typeof(TokenProbeHandler));
                return true;
            });
        var logger = new CapturingLogger();
        ushort port = GetFreePort();
        await using ServiceProvider services = new ServiceCollection().AddSingleton(packetProbe).BuildServiceProvider();
        var server = new ProbeServer(services,
            Options.Create(new HostingConfiguration { Host = "127.0.0.1", Port = port }), packets, logger);
        var connection = Substitute.For<IConnection>();
        var header = new NetworkPacketHeader { Type = NetworkPacketType.CMSG_PONG };

        // A packet that arrives while the shutdown is closing connections, as AuthServer's does.
        server.OnStopping = () => server.CallListener(connection, header, null);

        await server.StartAsync(CancellationToken.None);
        // On its own thread: the handler blocks inside its dispatch until the stop has finished.
        Task during = Task.Run(() => server.CallListener(connection, header, null));
        await packetProbe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await server.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        gate.Set();
        await during.WaitAsync(TimeSpan.FromSeconds(5));

        packetProbe.Gate = null;
        await server.CallListener(connection, header, null).WaitAsync(TimeSpan.FromSeconds(5));

        // The first ran, saw the stop and ended cancelled, which is not a handler failure; the one
        // inside OnStoppingAsync and the one after never reached a handler.
        Assert.Equal(1, packetProbe.Runs);
        Assert.Equal([true], packetProbe.TokensCancelled);
        Assert.Equal(0, logger.Count(Microsoft.Extensions.Logging.LogLevel.Error));
        connection.DidNotReceiveWithAnyArgs().Close();
        Assert.Empty(watchdog.Escaped);
    }

    // ── Accept failures outside a stop (#584) ───────────────────────────────

    private static SocketException SocketError(System.Net.Sockets.SocketError error) => new((int)error);

    /// <summary>Connects one client and waits until the server has started a connection for it.</summary>
    private static async Task<TcpClient> ConnectServedAsync(AcceptProbe probe, ushort port)
    {
        int before = probe.Started;
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await WaitUntilAsync(() => probe.Started > before);
        return client;
    }

    [Fact]
    public async Task Accept_the_next_client_after_an_accept_failure()
    {
        var probe = new AcceptProbe();
        (ProbeServer server, ServiceProvider services, ushort port) = CreateProbeServer(probe);
        await using ServiceProvider _ = services;
        server.AcceptFailures.Enqueue(SocketError(System.Net.Sockets.SocketError.TooManyOpenSockets));

        await server.StartAsync(CancellationToken.None);
        try
        {
            using TcpClient client = await ConnectServedAsync(probe, port);
            Assert.Equal(1, probe.Constructed);
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Back_off_with_a_capped_delay_that_a_success_resets()
    {
        var logger = new CapturingLogger();
        var probe = new AcceptProbe();
        (ProbeServer server, ServiceProvider services, ushort port) = CreateProbeServer(probe, logger);
        await using ServiceProvider _ = services;
        for (int i = 0; i < 9; i++)
            server.AcceptFailures.Enqueue(SocketError(System.Net.Sockets.SocketError.TooManyOpenSockets));

        await server.StartAsync(CancellationToken.None);
        try
        {
            using TcpClient first = await ConnectServedAsync(probe, port);
            Assert.Equal([100, 200, 400, 800, 1600, 3200, 5000, 5000, 5000],
                server.Delays.Select(d => (int)d.TotalMilliseconds));

            // A success resets the backoff: the loop is already waiting on the next real accept, so the
            // failure queued now is thrown by the accept after the second client's.
            server.AcceptFailures.Enqueue(SocketError(System.Net.Sockets.SocketError.NoBufferSpaceAvailable));
            using TcpClient second = await ConnectServedAsync(probe, port);
            using TcpClient third = await ConnectServedAsync(probe, port);
            Assert.Equal(100, (int)server.Delays.Last().TotalMilliseconds);

            // Inside one rate-limit window the first failure is warned about at once, and those after it
            // are reported, as a count, when the next client is accepted, so a burst is never lost.
            Assert.Equal(
                ["1 failures since the last warning", "8 more failed accepts", "1 more failed accepts"],
                logger.Entries.Where(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning)
                    .Select(e => e.Message.Contains("8 more failed accepts", StringComparison.Ordinal) ? "8 more failed accepts"
                        : e.Message.Contains("1 more failed accepts", StringComparison.Ordinal) ? "1 more failed accepts"
                        : e.Message.Contains("1 failures since the last warning", StringComparison.Ordinal) ? "1 failures since the last warning"
                        : e.Message));
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Retry_a_peer_reset_at_once_without_backing_off()
    {
        var probe = new AcceptProbe();
        (ProbeServer server, ServiceProvider services, ushort port) = CreateProbeServer(probe);
        await using ServiceProvider _ = services;
        server.AcceptFailures.Enqueue(SocketError(System.Net.Sockets.SocketError.ConnectionReset));

        await server.StartAsync(CancellationToken.None);
        try
        {
            using TcpClient client = await ConnectServedAsync(probe, port);
            Assert.Empty(server.Delays);
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task End_the_loop_promptly_when_stopped_during_a_backoff()
    {
        var probe = new AcceptProbe();
        (ProbeServer server, ServiceProvider services, ushort port) = CreateProbeServer(probe, realDelays: true);
        await using ServiceProvider _ = services;
        // Seven failures reach the 5 s cap; the stop lands inside one of the long waits.
        for (int i = 0; i < 7; i++)
            server.AcceptFailures.Enqueue(SocketError(System.Net.Sockets.SocketError.TooManyOpenSockets));

        await server.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => server.Delays.Count >= 5);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await server.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"The stop took {stopwatch.Elapsed}.");
        Assert.Equal(0, probe.Constructed);
    }

    [Fact]
    public async Task Keep_accepting_after_a_peer_resets_at_once()
    {
        var probe = new AcceptProbe();
        (ProbeServer server, ServiceProvider services, ushort port) = CreateProbeServer(probe);
        await using ServiceProvider _ = services;
        await server.StartAsync(CancellationToken.None);
        try
        {
            for (int i = 0; i < 5; i++)
            {
                // A zero linger closes with a reset instead of a FIN.
                var resetting = new TcpClient { LingerState = new LingerOption(true, 0) };
                await resetting.ConnectAsync(IPAddress.Loopback, port);
                resetting.Close();
            }

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            int clientPort = ((IPEndPoint)client.Client.LocalEndPoint!).Port;
            await WaitUntilAsync(() => probe.PeerPorts.Contains(clientPort));
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Count_each_accept_failure_outside_a_stop()
    {
        using var meter = new System.Diagnostics.Metrics.Meter($"accept-errors-{Guid.NewGuid()}");
        using var source = new System.Diagnostics.ActivitySource($"accept-errors-{Guid.NewGuid()}");
        var counted = new ConcurrentQueue<(long Value, string? Error)>();
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (ReferenceEquals(instrument.Meter, meter) && instrument.Name == "avalon.tcp.accept.errors")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            string? error = null;
            foreach (KeyValuePair<string, object?> tag in tags)
                if (tag.Key == "error.type")
                    error = tag.Value as string;
            counted.Enqueue((value, error));
        });
        listener.Start();

        var probe = new AcceptProbe();
        (ProbeServer server, ServiceProvider services, ushort port) =
            CreateProbeServer(probe, telemetry: new PacketDispatchTelemetry(source, meter));
        await using ServiceProvider _ = services;
        server.AcceptFailures.Enqueue(SocketError(System.Net.Sockets.SocketError.TooManyOpenSockets));
        server.AcceptFailures.Enqueue(SocketError(System.Net.Sockets.SocketError.ConnectionReset));

        await server.StartAsync(CancellationToken.None);
        try
        {
            using TcpClient client = await ConnectServedAsync(probe, port);
            Assert.Equal([(1L, "TooManyOpenSockets"), (1L, "ConnectionReset")], counted);
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Unregister_a_connection_whose_start_throws()
    {
        var logger = new CapturingLogger();
        var probe = new AcceptProbe { ThrowOnStart = true };
        (ProbeServer server, ServiceProvider services, ushort port) = CreateProbeServer(probe, logger);
        await using ServiceProvider _ = services;
        await server.StartAsync(CancellationToken.None);
        try
        {
            using TcpClient client = await ConnectServedAsync(probe, port);
            await WaitUntilAsync(() => logger.Count(Microsoft.Extensions.Logging.LogLevel.Error) == 1);
            Assert.Equal(0, server.ConnectionCount);
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Stop_twice_without_throwing()
    {
        var probe = new AcceptProbe();
        (ProbeServer server, ServiceProvider services, ushort _) = CreateProbeServer(probe);
        await using ServiceProvider __ = services;
        await server.StartAsync(CancellationToken.None);
        await server.StopAsync(CancellationToken.None);

        Exception? second = await Record.ExceptionAsync(() => server.StopAsync(CancellationToken.None));

        Assert.Null(second);
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
