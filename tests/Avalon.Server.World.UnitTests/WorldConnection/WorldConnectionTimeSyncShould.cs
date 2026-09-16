// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System;
using System.Net;
using System.Net.Sockets;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.World;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.WorldConnection;

/// <summary>
/// The clock offset a client is told. Every term of it must come from the exchange being measured:
/// reading the previous exchange's client stamp instead reports the gap between pings, which for the
/// ten-second cadence is a ten-second clock skew between two processes on one machine.
/// </summary>
public class WorldConnectionTimeSyncShould : IDisposable
{
    private const long TicksPerMs = TimeSpan.TicksPerMillisecond;

    // Every timestamp is now stated by the caller, so these are exact -- but a tolerance keeps the
    // cases about the arithmetic rather than about tick precision. Ten milliseconds still fails by
    // three orders of magnitude if a ping interval leaks in.
    private const long ToleranceTicks = 10 * TicksPerMs;

    private readonly Avalon.World.WorldConnection _connection;
    private readonly TcpClient _serverSide;

    public WorldConnectionTimeSyncShould()
    {
        var server = Substitute.For<IWorldServer, IServerBase>();
        ((IServerBase)server).SendBufferCapacity.Returns(256);

        var (clientSide, serverSide) = CreateLoopbackPair();
        _serverSide = serverSide;

        _connection = new Avalon.World.WorldConnection(
            server,
            clientSide,
            NullLoggerFactory.Instance,
            Substitute.For<IPacketReader>());
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
        _serverSide.Dispose();
    }

    private static (TcpClient clientSide, TcpClient serverSide) CreateLoopbackPair()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint!).Port;
        var clientSide = new TcpClient();
        clientSide.Connect(IPAddress.Loopback, port);
        var serverSide = listener.AcceptTcpClient();
        listener.Stop();
        return (clientSide, serverSide);
    }

    /// <summary>A pong whose client agrees with our clock and answered instantly: the offset is zero.</summary>
    private void ExchangeAt(long serverSentTicks, long oneWayTicks)
    {
        long clientTouchedAt = serverSentTicks + oneWayTicks;   // received and answered in the same instant
        _connection.OnPongReceived(serverSentTicks, clientTouchedAt, clientTouchedAt,
            clientTouchedAt + oneWayTicks);
    }

    [Fact]
    public void ReportNoOffset_WhenTheClientClockAgrees()
    {
        long now = DateTime.UtcNow.Ticks;
        ExchangeAt(now - 20 * TicksPerMs, 10 * TicksPerMs);

        Assert.InRange(_connection.TimeSyncOffset, -ToleranceTicks, ToleranceTicks);
    }

    [Fact]
    public void ReportNoOffset_OnASecondExchangeTenSecondsLater()
    {
        // The first exchange leaves the connection holding a client stamp ten seconds old. An offset
        // computed against THAT rather than against the second exchange's own stamp comes back as ten
        // seconds — which is the defect, and it only appears from the second ping onwards.
        long now = DateTime.UtcNow.Ticks;
        ExchangeAt(now - 10 * TimeSpan.TicksPerSecond, 10 * TicksPerMs);
        ExchangeAt(now - 20 * TicksPerMs, 10 * TicksPerMs);

        Assert.InRange(_connection.TimeSyncOffset, -ToleranceTicks, ToleranceTicks);
    }

    [Fact]
    public void ReportTheOffset_WhenTheClientClockIsAhead()
    {
        // A client running one second fast: the offset is what a consumer would subtract to reach
        // server time, so it is NEGATIVE by that second. Without this the zero-offset cases above
        // would also pass an implementation that reported a constant nothing.
        long now = DateTime.UtcNow.Ticks;
        long skew = TimeSpan.TicksPerSecond;
        long serverSent = now - 20 * TicksPerMs;
        long clientTouchedAt = serverSent + 10 * TicksPerMs + skew;
        _connection.OnPongReceived(serverSent, clientTouchedAt, clientTouchedAt,
            serverSent + 20 * TicksPerMs);

        Assert.InRange(_connection.TimeSyncOffset, -skew - ToleranceTicks, -skew + ToleranceTicks);
    }

    [Fact]
    public void ChargeNoneOfTheTickWait_WhenThePongWaitedForOne()
    {
        // The pong lands on the socket a millisecond after it was sent and is handled a full 60 Hz
        // tick later. The round trip is the wire, not the wait, and the offset stays at zero because
        // neither leg was inflated.
        // Anchored a tick in the PAST, which is what makes this case discriminating: the exchange
        // finished 16 ms ago and is only being handled now, so a t3 read from the clock here differs
        // from the arrival by exactly the wait this is about.
        long handledAt = DateTime.UtcNow.Ticks;
        long serverSent = handledAt - 17 * TicksPerMs;
        long clientTouchedAt = serverSent + 1 * TicksPerMs;
        long arrived = clientTouchedAt + 1 * TicksPerMs;

        _connection.OnPongReceived(serverSent, clientTouchedAt, clientTouchedAt, arrived);

        Assert.InRange(_connection.RoundTripTime, 0, 4 * TicksPerMs);
        Assert.InRange(_connection.TimeSyncOffset, -TicksPerMs, TicksPerMs);
    }

    [Fact]
    public void HandOutAnInitialPingRequestOnce()
    {
        // The tick asks every connection every tick, so a request that did not clear would ping on
        // all of them -- once per 16 ms rather than once per ten seconds.
        Assert.False(_connection.TakeInitialTimeSyncPingRequest());

        _connection.RequestInitialTimeSyncPing();

        Assert.True(_connection.TakeInitialTimeSyncPingRequest());
        Assert.False(_connection.TakeInitialTimeSyncPingRequest());
    }

    [Fact]
    public void ReportTheRoundTrip_AsBothLegsTogether()
    {
        long now = DateTime.UtcNow.Ticks;
        ExchangeAt(now - 20 * TicksPerMs, 10 * TicksPerMs);

        // Ten milliseconds out and ten back, whatever the clocks say.
        Assert.InRange(_connection.RoundTripTime, 20 * TicksPerMs - ToleranceTicks, 20 * TicksPerMs + ToleranceTicks);
    }
}
