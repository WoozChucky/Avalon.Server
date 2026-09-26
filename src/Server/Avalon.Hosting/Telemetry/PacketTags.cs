// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System;
using System.Net;

namespace Avalon.Hosting.Telemetry;

/// <summary>Who a packet came from, as its span, metrics and log scope describe it.</summary>
public readonly record struct PacketTags(Guid ConnectionId, string ClientAddress, long? AccountId, uint? CharacterId)
{
    /// <summary>The address without its port: "203.0.113.7:5000" is "203.0.113.7".</summary>
    public static string AddressOf(string remoteEndPoint) =>
        IPEndPoint.TryParse(remoteEndPoint, out IPEndPoint? endPoint) ? endPoint.Address.ToString() : remoteEndPoint;
}
