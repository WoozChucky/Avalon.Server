using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Avalon.LoadTest.World;

/// <summary>
/// The binary PROXY protocol v2 header (haproxy.org/download/2.8/doc/proxy-protocol.txt) a bot writes first on a world
/// connection it dials directly (<c>--proxy-header</c>), in place of the edge proxy that otherwise writes it: version 2,
/// command PROXY, TCP over IPv4 or IPv6, the bot's own end as the source and the dialled end as the destination, no
/// TLVs. The world reads it only from a peer it trusts to send one (its <c>proxyProtocol.trustedProxies</c>), which the
/// node's load balancer makes every connection.
/// </summary>
public static class ProxyHeaderV2
{
    private static ReadOnlySpan<byte> Signature => [0x0D, 0x0A, 0x0D, 0x0A, 0x00, 0x0D, 0x0A, 0x51, 0x55, 0x49, 0x54, 0x0A];

    /// <summary>Version 2 in the high nibble, command PROXY in the low.</summary>
    private const byte VersionAndCommand = 0x21;

    /// <summary>AF_INET in the high nibble, SOCK_STREAM in the low.</summary>
    private const byte TcpOverIPv4 = 0x11;

    /// <summary>AF_INET6 in the high nibble, SOCK_STREAM in the low.</summary>
    private const byte TcpOverIPv6 = 0x21;

    /// <summary>
    /// The header for a TCP connection from <paramref name="source"/> to <paramref name="destination"/>. A dual-mode
    /// socket reports an IPv4 connection's ends as IPv4-mapped IPv6 addresses: those are written as IPv4, so the family
    /// matches the connection on the wire.
    /// </summary>
    /// <exception cref="ArgumentException">The two ends are not of one address family.</exception>
    public static byte[] Build(IPEndPoint source, IPEndPoint destination)
    {
        IPAddress from = Unmapped(source.Address);
        IPAddress to = Unmapped(destination.Address);
        if (from.AddressFamily != to.AddressFamily)
            throw new ArgumentException($"The connection's ends {from} and {to} are not of one address family.", nameof(destination));

        bool v4 = from.AddressFamily == AddressFamily.InterNetwork;
        int addressLength = v4 ? 4 : 16;
        int bodyLength = 2 * addressLength + 4;
        byte[] header = new byte[16 + bodyLength];
        Span<byte> span = header;

        Signature.CopyTo(span);
        span[12] = VersionAndCommand;
        span[13] = v4 ? TcpOverIPv4 : TcpOverIPv6;
        BinaryPrimitives.WriteUInt16BigEndian(span[14..], (ushort)bodyLength);

        // Source address, destination address, source port, destination port.
        Span<byte> body = span[16..];
        from.TryWriteBytes(body[..addressLength], out _);
        to.TryWriteBytes(body.Slice(addressLength, addressLength), out _);
        BinaryPrimitives.WriteUInt16BigEndian(body[(2 * addressLength)..], (ushort)source.Port);
        BinaryPrimitives.WriteUInt16BigEndian(body[(2 * addressLength + 2)..], (ushort)destination.Port);
        return header;
    }

    private static IPAddress Unmapped(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
