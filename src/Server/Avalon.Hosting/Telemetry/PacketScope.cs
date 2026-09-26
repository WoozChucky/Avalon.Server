// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using Avalon.Network.Packets.Abstractions;

namespace Avalon.Hosting.Telemetry;

/// <summary>
/// The log scope of one handler run: PacketType, ConnectionId, and AccountId and CharacterId when
/// known. One small object per packet instead of a list, its array and three boxes; the values are
/// boxed only when a log provider actually reads the scope, which most packets never cause.
/// </summary>
internal sealed class PacketScope(NetworkPacketType type, PacketTags tags) : IReadOnlyList<KeyValuePair<string, object?>>
{
    public int Count => 2 + (tags.AccountId.HasValue ? 1 : 0) + (tags.CharacterId.HasValue ? 1 : 0);

    public KeyValuePair<string, object?> this[int index] => index switch
    {
        0 => new("PacketType", PacketDispatchTelemetry.NameOf(type)),
        1 => new("ConnectionId", tags.ConnectionId),
        2 when tags.AccountId is { } account => new("AccountId", account),
        2 when tags.CharacterId is { } character => new("CharacterId", character),
        3 when tags.AccountId.HasValue && tags.CharacterId is { } character => new("CharacterId", character),
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
            yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"PacketType:{type} ConnectionId:{tags.ConnectionId}";
}
