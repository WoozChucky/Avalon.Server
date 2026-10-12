using Avalon.Network.Packets.Abstractions;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>
/// The log lines of the high-rate packets' handlers (<c>CMSG_PLAYER_INPUT</c>, <c>CMSG_PONG</c>). Those packets open no
/// log scope (#875), so each line carries its connection and packet type as fields of its own.
/// </summary>
internal static partial class HighRatePacketLog
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Dropped {PacketType} on connection {ConnectionId}: character {Name} is dead")]
    public static partial void DroppedFromDeadCharacter(ILogger logger, NetworkPacketType packetType, Guid connectionId, string name);

    [LoggerMessage(Level = LogLevel.Error, Message = "{PacketType} on connection {ConnectionId}: no instance {InstanceId}")]
    public static partial void InstanceNotFound(ILogger logger, NetworkPacketType packetType, Guid connectionId, Guid instanceId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[{CharName}] Latency changed: {Latency}ms -> {NewLatency}ms ({PacketType} on connection {ConnectionId})")]
    public static partial void LatencyChanged(ILogger logger, string? charName, long latency, long newLatency,
        NetworkPacketType packetType, Guid connectionId);

    [LoggerMessage(Level = LogLevel.Trace,
        Message = "[{CharName}] RTT: {Rtt}ticks, Latency: {Latency}ms ({PacketType} on connection {ConnectionId})")]
    public static partial void RoundTrip(ILogger logger, string? charName, long rtt, long latency,
        NetworkPacketType packetType, Guid connectionId);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Ignored {PacketType} on connection {ConnectionId}: its echo is the stamp of no unanswered ping")]
    public static partial void PongAnswersNoPing(ILogger logger, NetworkPacketType packetType, Guid connectionId);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Ignored {PacketType} on connection {ConnectionId}: it arrived before its ping was stamped (the clock was set back)")]
    public static partial void PongBeforeItsPing(ILogger logger, NetworkPacketType packetType, Guid connectionId);
}
