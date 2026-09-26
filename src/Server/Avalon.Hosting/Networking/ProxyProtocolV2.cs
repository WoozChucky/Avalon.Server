// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Avalon.Hosting.Networking;

/// <summary>What a PROXY v2 header said about the connection.</summary>
/// <param name="IsLocal">The proxy opened the connection itself (e.g. a health check); keep the peer address.</param>
/// <param name="Source">The real client, or null when the header carries no routable TCP address.</param>
public sealed record ProxyHeader(bool IsLocal, IPEndPoint? Source);

/// <summary>
/// Reads the binary PROXY protocol v2 header (haproxy.org/download/2.8/doc/proxy-protocol.txt) that
/// an L4 proxy writes before any application byte. Reads exactly the header and nothing after it,
/// so the stream is left at the first byte of the proxied connection (the TLS ClientHello for auth).
/// </summary>
public static class ProxyProtocolV2
{
    /// <summary>Largest address block accepted: an AF_UNIX pair is 216 bytes; the rest is TLVs.</summary>
    public const int MaxBodyLength = 1024;

    private static ReadOnlySpan<byte> Signature => [0x0D, 0x0A, 0x0D, 0x0A, 0x00, 0x0D, 0x0A, 0x51, 0x55, 0x49, 0x54, 0x0A];

    private const byte CommandLocal = 0x0;
    private const byte CommandProxy = 0x1;
    private const byte FamilyInet = 0x1;
    private const byte FamilyInet6 = 0x2;
    private const byte TransportStream = 0x1;

    /// <exception cref="InvalidDataException">Not a PROXY v2 header, or a malformed one.</exception>
    /// <exception cref="EndOfStreamException">The stream ended inside the header.</exception>
    public static async Task<ProxyHeader> ReadAsync(Stream stream, CancellationToken ct)
    {
        byte[] prefix = new byte[16];
        await stream.ReadExactlyAsync(prefix, ct).ConfigureAwait(false);

        if (!prefix.AsSpan(0, 12).SequenceEqual(Signature))
            throw new InvalidDataException("Connection did not start with a PROXY protocol v2 signature.");

        int version = prefix[12] >> 4;
        int command = prefix[12] & 0x0F;
        if (version != 2)
            throw new InvalidDataException($"Unsupported PROXY protocol version {version}.");
        if (command != CommandLocal && command != CommandProxy)
            throw new InvalidDataException($"Unknown PROXY protocol command {command}.");

        int family = prefix[13] >> 4;
        int transport = prefix[13] & 0x0F;
        int length = BinaryPrimitives.ReadUInt16BigEndian(prefix.AsSpan(14, 2));
        if (length > MaxBodyLength)
            throw new InvalidDataException($"PROXY protocol header of {length} bytes exceeds {MaxBodyLength}.");

        byte[] body = new byte[length];
        await stream.ReadExactlyAsync(body, ct).ConfigureAwait(false);

        if (command == CommandLocal)
            return new ProxyHeader(IsLocal: true, Source: null);

        if (transport != TransportStream)
            return new ProxyHeader(IsLocal: false, Source: null);

        return family switch
        {
            FamilyInet => new ProxyHeader(false, ReadEndPoint(body, addressLength: 4)),
            FamilyInet6 => new ProxyHeader(false, ReadEndPoint(body, addressLength: 16)),
            _ => new ProxyHeader(false, null),
        };
    }

    private static IPEndPoint ReadEndPoint(byte[] body, int addressLength)
    {
        // Source address, destination address, source port, destination port; TLVs may follow.
        int needed = 2 * addressLength + 4;
        if (body.Length < needed)
            throw new InvalidDataException($"PROXY protocol address block of {body.Length} bytes is shorter than {needed}.");

        var address = new IPAddress(body.AsSpan(0, addressLength));
        int port = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(2 * addressLength, 2));
        return new IPEndPoint(address, port);
    }
}
