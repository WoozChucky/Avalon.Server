using System.Net;
using Avalon.Hosting.Networking;
using Xunit;

namespace Avalon.Server.Auth.UnitTests.Networking;

/// <summary>
/// The edge proxy (Caddy's layer4 app) prefixes every game connection with a PROXY protocol v2
/// header carrying the real client address. Without it every player shares the proxy's address,
/// and the per-source login limits lock everyone out together (#524).
/// </summary>
public class ProxyProtocolV2Should
{
    private static readonly byte[] Signature = [0x0D, 0x0A, 0x0D, 0x0A, 0x00, 0x0D, 0x0A, 0x51, 0x55, 0x49, 0x54, 0x0A];

    internal static byte[] Header(byte versionCommand, byte familyProtocol, byte[] body)
    {
        byte[] header = new byte[16 + body.Length];
        Signature.CopyTo(header, 0);
        header[12] = versionCommand;
        header[13] = familyProtocol;
        header[14] = (byte)(body.Length >> 8);
        header[15] = (byte)body.Length;
        body.CopyTo(header, 16);
        return header;
    }

    internal static byte[] ProxyTcp4(string source, ushort sourcePort, string destination = "10.42.0.9", ushort destinationPort = 21000, byte[]? tlvs = null)
    {
        byte[] body = new byte[12 + (tlvs?.Length ?? 0)];
        IPAddress.Parse(source).GetAddressBytes().CopyTo(body, 0);
        IPAddress.Parse(destination).GetAddressBytes().CopyTo(body, 4);
        body[8] = (byte)(sourcePort >> 8);
        body[9] = (byte)sourcePort;
        body[10] = (byte)(destinationPort >> 8);
        body[11] = (byte)destinationPort;
        tlvs?.CopyTo(body, 12);
        return Header(0x21, 0x11, body);
    }

    private static Task<ProxyHeader> Read(byte[] bytes, params byte[] after) =>
        ProxyProtocolV2.ReadAsync(new MemoryStream([.. bytes, .. after]), CancellationToken.None);

    [Fact]
    public async Task Read_the_client_address_of_a_TCP_over_IPv4_connection()
    {
        ProxyHeader header = await Read(ProxyTcp4("203.0.113.7", 51000));

        Assert.False(header.IsLocal);
        Assert.Equal(new IPEndPoint(IPAddress.Parse("203.0.113.7"), 51000), header.Source);
    }

    [Fact]
    public async Task Read_the_client_address_of_a_TCP_over_IPv6_connection()
    {
        byte[] body = new byte[36];
        IPAddress.Parse("2001:db8::7").GetAddressBytes().CopyTo(body, 0);
        IPAddress.Parse("2001:db8::1").GetAddressBytes().CopyTo(body, 16);
        body[32] = 0xC7; body[33] = 0x38; // 51000
        body[34] = 0x52; body[35] = 0x08; // 21000

        ProxyHeader header = await Read(Header(0x21, 0x21, body));

        Assert.Equal(new IPEndPoint(IPAddress.Parse("2001:db8::7"), 51000), header.Source);
    }

    [Fact]
    public async Task Report_LOCAL_connections_without_a_client_address()
    {
        ProxyHeader header = await Read(Header(0x20, 0x00, []));

        Assert.True(header.IsLocal);
        Assert.Null(header.Source);
    }

    [Fact]
    public async Task Skip_TLVs_and_leave_the_stream_at_the_first_byte_after_the_header()
    {
        byte[] tlv = [0x04, 0x00, 0x02, 0xAB, 0xCD]; // type NOOP, length 2
        var stream = new MemoryStream([.. ProxyTcp4("198.51.100.4", 40000, tlvs: tlv), 0x42]);

        ProxyHeader header = await ProxyProtocolV2.ReadAsync(stream, CancellationToken.None);

        Assert.Equal(new IPEndPoint(IPAddress.Parse("198.51.100.4"), 40000), header.Source);
        Assert.Equal(0x42, stream.ReadByte());
    }

    [Fact]
    public async Task Keep_no_address_for_families_it_does_not_route()
    {
        ProxyHeader header = await Read(Header(0x21, 0x31, new byte[216])); // AF_UNIX stream

        Assert.False(header.IsLocal);
        Assert.Null(header.Source);
    }

    [Fact]
    public async Task Refuse_a_stream_that_does_not_start_with_the_signature()
    {
        byte[] notProxy = new byte[16];
        "\u0016\u0003\u0001 a TLS hello"u8.ToArray().CopyTo(notProxy, 0);

        await Assert.ThrowsAsync<InvalidDataException>(() => Read(notProxy));
    }

    [Fact]
    public async Task Refuse_a_version_other_than_2()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(Header(0x11, 0x11, new byte[12])));
    }

    [Fact]
    public async Task Refuse_an_address_block_shorter_than_its_family_needs()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(Header(0x21, 0x11, new byte[8])));
    }

    [Fact]
    public async Task Refuse_a_header_longer_than_the_cap()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(Header(0x21, 0x11, new byte[ProxyProtocolV2.MaxBodyLength + 1])));
    }

    [Fact]
    public async Task Fail_on_a_header_cut_short()
    {
        byte[] full = ProxyTcp4("203.0.113.7", 51000);

        await Assert.ThrowsAsync<EndOfStreamException>(() => Read(full[..20]));
    }

    [Fact]
    public async Task Report_a_stream_that_ends_before_its_first_byte_as_no_header_sent()
    {
        await Assert.ThrowsAsync<ProxyHeaderNotSentException>(() => Read([]));
    }

    [Fact]
    public async Task Report_a_stream_that_ends_after_its_first_byte_as_a_header_cut_short()
    {
        byte[] full = ProxyTcp4("203.0.113.7", 51000);

        // ThrowsAsync matches the exact type, so this also proves it is not ProxyHeaderNotSentException.
        await Assert.ThrowsAsync<EndOfStreamException>(() => Read(full[..1]));
    }
}
