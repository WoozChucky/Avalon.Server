using Avalon.Configuration;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.GameAuth;

public sealed class GameWorkloadConfigurationShould
{
    private static GameServerDefinition Server(string id = "world-1", ushort world = 1) => new()
    {
        ServerId = id,
        WorldId = world,
        TlsServerName = "localhost",
    };
    private static GameWorkloadConfiguration Configured(params GameServerDefinition[] servers) => new()
    {
        CertificatePath = "/certs/tls.crt",
        KeyPath = "/certs/tls.key",
        CaCertificatePath = "/ca/ca.crt",
        Servers = [.. servers],
    };
    [Fact]
    public void Require_unique_server_and_world_bindings()
    {
        Configured(Server()).Validate();
        Assert.Throws<InvalidOperationException>(() => Configured(Server(), Server("world-1", 2)).Validate());
        Assert.Throws<InvalidOperationException>(() => Configured(Server(), Server("world-2", 1)).Validate());
    }
    [Theory]
    [InlineData(0, "localhost")]
    [InlineData(1, "https://attacker.test/")]
    public void Refuse_unusable_endpoints(ushort world, string name)
    {
        GameServerDefinition server = Server(world: world); server.TlsServerName = name;
        Assert.Throws<InvalidOperationException>(() => Configured(server).Validate());
    }
    [Theory]
    [InlineData(true, true, true, 1, true)]
    [InlineData(false, true, true, 1, false)]
    [InlineData(true, false, true, 1, false)]
    [InlineData(true, true, false, 1, false)]
    [InlineData(false, false, false, 0, true)]
    public void Require_the_certificate_paths_when_game_servers_are_configured(bool certificate, bool key, bool ca, int servers, bool accepted)
    {
        var configuration = new GameWorkloadConfiguration
        {
            CertificatePath = certificate ? "/certs/tls.crt" : "",
            KeyPath = key ? "/certs/tls.key" : "",
            CaCertificatePath = ca ? "/ca/ca.crt" : "",
            Servers = [.. Enumerable.Range(1, servers).Select(n => Server($"world-{n}", (ushort)n))],
        };
        if (accepted) configuration.Validate();
        else Assert.Throws<InvalidOperationException>(configuration.Validate);
    }
}
