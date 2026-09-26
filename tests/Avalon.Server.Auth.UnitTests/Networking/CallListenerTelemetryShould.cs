using System.Diagnostics;
using System.Diagnostics.Metrics;
using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Avalon.Hosting.Telemetry;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Avalon.Server.Auth.UnitTests.Networking;

/// <summary>CallListener runs every auth handler, and the world packets handled immediately.</summary>
public sealed class CallListenerTelemetryShould : IDisposable
{
    private readonly string _name = $"test-{Guid.NewGuid()}";
    private readonly ActivitySource _source;
    private readonly Meter _meter;
    private readonly ActivityListener _listener;
    private readonly List<Activity> _spans = [];
    private readonly IConnection _connection = Substitute.For<IConnection>();

    public CallListenerTelemetryShould()
    {
        _source = new ActivitySource(_name);
        _meter = new Meter(_name);
        _listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == _name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => _spans.Add(a),
        };
        ActivitySource.AddActivityListener(_listener);
        _connection.Id.Returns(Guid.NewGuid());
        _connection.RemoteEndPoint.Returns("203.0.113.7:5000");
    }

    public void Dispose()
    {
        _listener.Dispose();
        _source.Dispose();
        _meter.Dispose();
    }

    private sealed class OkHandler : IPacketHandlerNew
    {
        public Task ExecuteAsync(object context, CancellationToken token) => Task.CompletedTask;
    }

    private sealed class ThrowingHandler : IPacketHandlerNew
    {
        public Task ExecuteAsync(object context, CancellationToken token) => throw new InvalidOperationException("boom");
    }

    private sealed class TestServer(IPacketManager packets, PacketDispatchTelemetry telemetry)
        : ServerBase<IConnection>(packets, NullLogger.Instance, new ServiceCollection().BuildServiceProvider(),
            Options.Create(new HostingConfiguration { Host = "127.0.0.1", Port = 0 }), telemetry)
    {
        protected override object GetContextPacket(IConnection connection, object? packet, Type packetType) => new();
        protected override Task OnStoppingAsync(CancellationToken stoppingToken) => Task.CompletedTask;
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }

    private TestServer Server(Type handler)
    {
        IPacketManager packets = Substitute.For<IPacketManager>();
        packets.TryGetPacketInfo(NetworkPacketType.CMSG_AUTH, out Arg.Any<PacketInfo>())
            .Returns(call =>
            {
                call[1] = new PacketInfo(typeof(object), handler);
                return true;
            });
        return new TestServer(packets, new PacketDispatchTelemetry(_source, _meter));
    }

    private static NetworkPacketHeader Auth => new() { Type = NetworkPacketType.CMSG_AUTH };

    [Fact]
    public async Task Start_a_span_for_each_packet_it_dispatches()
    {
        await Server(typeof(OkHandler)).CallListener(_connection, Auth, null);

        Activity span = Assert.Single(_spans);
        Assert.Equal("packet CMSG_AUTH", span.DisplayName);
        Assert.Equal("203.0.113.7", span.GetTagItem("client.address"));
        Assert.Equal(_connection.Id.ToString(), span.GetTagItem("avalon.connection.id"));
        Assert.Equal("ok", span.GetTagItem("avalon.outcome"));
    }

    [Fact]
    public async Task Mark_the_span_as_an_error_and_still_close_the_connection_when_the_handler_throws()
    {
        await Server(typeof(ThrowingHandler)).CallListener(_connection, Auth, null);

        Activity span = Assert.Single(_spans);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal("error", span.GetTagItem("avalon.outcome"));
        _connection.Received(1).Close();
    }

    [Fact]
    public async Task Start_no_span_for_a_packet_without_a_handler()
    {
        IPacketManager packets = Substitute.For<IPacketManager>();
        await new TestServer(packets, new PacketDispatchTelemetry(_source, _meter)).CallListener(_connection, Auth, null);

        Assert.Empty(_spans);
    }
}
