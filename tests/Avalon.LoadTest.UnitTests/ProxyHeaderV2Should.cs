using System.Net;
using Avalon.Hosting.Networking;
using Avalon.LoadTest.World;
using Xunit;

namespace Avalon.LoadTest.UnitTests;

public class ProxyHeaderV2Should
{
    /// <summary>
    /// The header a bot writes with <c>--proxy-header</c>, read by the world's own PROXY v2 reader: the bot's end comes
    /// back as the client, and the reader stops at the TLS record that follows. The ends as a dual-mode socket reports
    /// an IPv4 connection (IPv4-mapped IPv6) come back as the plain IPv4 client.
    /// </summary>
    [Theory]
    [InlineData("10.10.1.21", 50123, "10.10.1.15", 21004, "10.10.1.21")]
    [InlineData("::ffff:10.10.1.21", 50123, "::ffff:10.10.1.15", 21004, "10.10.1.21")]
    [InlineData("fd00::21", 61000, "fd00::15", 21004, "fd00::21")]
    public async Task Be_read_by_the_world_as_the_bots_own_end(
        string source, int sourcePort, string destination, int destinationPort, string expectedClient)
    {
        byte[] header = ProxyHeaderV2.Build(
            new IPEndPoint(IPAddress.Parse(source), sourcePort), new IPEndPoint(IPAddress.Parse(destination), destinationPort));
        const byte TlsHandshakeRecord = 0x16;
        using var stream = new MemoryStream([.. header, TlsHandshakeRecord]);

        ProxyHeader read = await ProxyProtocolV2.ReadAsync(stream, CancellationToken.None);

        Assert.False(read.IsLocal);
        Assert.Equal(new IPEndPoint(IPAddress.Parse(expectedClient), sourcePort), read.Source);
        Assert.Equal(TlsHandshakeRecord, stream.ReadByte());
    }
}
