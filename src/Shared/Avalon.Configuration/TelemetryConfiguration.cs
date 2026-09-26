// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

namespace Avalon.Configuration;

/// <summary>Telemetry settings, under <c>Hosting:Telemetry</c>.</summary>
public class TelemetryConfiguration
{
    /// <summary>
    /// Packet types handled without a span (their duration and errors are still counted), by
    /// NetworkPacketType name. Null keeps the defaults: CMSG_PLAYER_INPUT and CMSG_PONG, sent many
    /// times a second per player. An array, not a list: the binder appends to a list that already
    /// holds the defaults instead of replacing it.
    /// </summary>
    public string[]? NoSpanPacketTypes { get; set; }
}
