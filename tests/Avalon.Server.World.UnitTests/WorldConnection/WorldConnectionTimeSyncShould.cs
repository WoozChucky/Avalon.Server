// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System;
using System.Net;
using System.Net.Sockets;
using Avalon.Common.Cryptography;
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

    // The offset is derived from a timestamp the method reads itself, so it cannot be exact. A
    // symmetric path should still land within a millisecond or two of zero; ten is a wide net that
    // still fails by three orders of magnitude if a ping interval leaks in.
    private const long ToleranceTicks = 10 * TicksPerMs;

    private readonly Avalon.World.WorldConnection _connection;
    private readonly TcpClient _serverSide;

    public WorldConnectionTimeSyncShould()
    {
        var server = Substitute.For<IWorldServer, IServerBase>();
        ((IServerBase)server).Crypto.Returns(new CryptoManager());
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
        _connection.OnPongReceived(serverSentTicks, clientTouchedAt, clientTouchedAt);
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
        _connection.OnPongReceived(serverSent, clientTouchedAt, clientTouchedAt);

        Assert.InRange(_connection.TimeSyncOffset, -skew - ToleranceTicks, -skew + ToleranceTicks);
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
