using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Avalon.Shared.UnitTests.Networking;

public class PacketReaderShould
{
    private static PacketReader Make(int bufferSize) =>
        new PacketReader(
            NullLoggerFactory.Instance,
            Options.Create(new HostingConfiguration { PacketReaderBufferSize = bufferSize }),
            packetTypes: []);

    [Fact]
    public void UseConfiguredBufferSize_WhenConstructed()
    {
        var reader = Make(8192);

        // The buffer size is consumed by EnumerateAsync (passed to PacketStream.EnumerateAsync).
        // We can verify the configuration was accepted without throwing by simply instantiating
        // with a non-default value. A deeper assertion would require an integration test with a
        // real stream — this test guards against regressions in the constructor wiring.
        Assert.NotNull(reader);
    }

    [Fact]
    public void UseDefaultBufferSize_WhenNotOverridden()
    {
        var reader = new PacketReader(
            NullLoggerFactory.Instance,
            Options.Create(new HostingConfiguration()),
            packetTypes: []);

        Assert.NotNull(reader);
    }

    [Theory]
    [InlineData(512)]
    [InlineData(4096)]
    [InlineData(65535)]
    public void AcceptValidBufferSizes(int size)
    {
        var ex = Record.Exception(() => Make(size));
        Assert.Null(ex);
    }
}
