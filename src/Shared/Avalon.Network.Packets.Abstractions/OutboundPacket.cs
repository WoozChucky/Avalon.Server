namespace Avalon.Network.Packets.Abstractions;

/// <summary>
/// A packet on its way out (#875): its header, and its message encoded, still plain, in a pooled segment. Sealing (when
/// the connection seals) and framing happen on the send path; nothing here allocates per packet.
/// </summary>
/// <remarks>
/// Whoever holds one owns one reference to its payload. <c>IConnection.Send</c> takes it over: the connection writes the
/// payload and releases it, or refuses the packet and releases it at once. A broadcast hands each recipient a reference
/// of its own (<see cref="Share" />) and releases its own when done. Never read a packet after giving it away.
/// </remarks>
public readonly struct OutboundPacket
{
    public OutboundPacket(NetworkPacketHeader header, PayloadSegment? payload)
    {
        Header = header;
        Payload = payload;
    }

    public NetworkPacketHeader Header { get; }

    /// <summary>The encoded message; null only for a marker the send path fills in itself (the time-sync ping).</summary>
    public PayloadSegment? Payload { get; }

    public int PayloadLength => Payload?.Length ?? 0;

    public ReadOnlyMemory<byte> PayloadMemory => Payload?.Memory ?? ReadOnlyMemory<byte>.Empty;

    /// <summary>The header and the payload, as the per-packet byte counters count them.</summary>
    public int Size => Header.Size + PayloadLength;

    /// <summary>One more reference to the same payload, for one more recipient. Take it before handing the packet on.</summary>
    public OutboundPacket Share()
    {
        Payload?.AddReference();
        return this;
    }

    /// <summary>Gives up this holder's reference; the last one returns the payload to its pool.</summary>
    public void Release() => Payload?.Release();
}
