using Avalon.Configuration;
using Xunit;

namespace Avalon.Api.UnitTests.GameAuth;

public sealed class GameWorkloadConfigurationShould
{
    private static GameServerDefinition Server(string id = "world-1", ushort world = 1, char client = 'B') => new()
    {
        ServerId = id, WorldId = world, TlsServerName = "localhost", TlsCertificateSha256 = new string('A', 64), ClientCertificateSha256 = new string(client, 64),
    };
    [Fact]
    public void Require_unique_server_world_and_workload_certificate_bindings()
    {
        var options = new GameWorkloadConfiguration { Servers = [Server()] };
        options.Validate();
        Assert.Throws<InvalidOperationException>(() => new GameWorkloadConfiguration { Servers = [Server(), Server("world-1", 2, 'C')] }.Validate());
        Assert.Throws<InvalidOperationException>(() => new GameWorkloadConfiguration { Servers = [Server(), Server("world-2", 1, 'C')] }.Validate());
        Assert.Throws<InvalidOperationException>(() => new GameWorkloadConfiguration { Servers = [Server(), Server("world-2", 2)] }.Validate());
    }
    [Theory]
    [InlineData(0, "localhost", "short")]
    [InlineData(1, "https://attacker.test/", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(1, "localhost", "not-a-certificate-digest")]
    public void Refuse_unusable_endpoints_and_pins(ushort world, string name, string pin)
    {
        var server = Server(world: world); server.TlsServerName = name; server.TlsCertificateSha256 = pin;
        Assert.Throws<InvalidOperationException>(() => new GameWorkloadConfiguration { Servers = [server] }.Validate());
    }
}
