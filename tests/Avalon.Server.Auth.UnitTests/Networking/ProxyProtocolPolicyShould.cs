using System.Net;
using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Avalon.Server.Auth.UnitTests.Networking;

/// <summary>
/// Only peers inside the trusted networks may assert a client address; anyone else is a direct
/// client whose socket address is the truth.
/// </summary>
public class ProxyProtocolPolicyShould
{
    private static ProxyProtocolPolicy Policy(bool enabled, params string[] trusted) =>
        ProxyProtocolPolicy.From(new ProxyProtocolConfiguration { Enabled = enabled, TrustedProxies = [.. trusted] });

    [Theory]
    [InlineData("10.42.0.1", true)]
    [InlineData("10.42.200.9", true)]
    [InlineData("10.43.0.1", false)]
    [InlineData("203.0.113.7", false)]
    public void Trust_only_peers_inside_the_configured_networks(string peer, bool trusted)
    {
        Assert.Equal(trusted, Policy(true, "10.42.0.0/16").IsTrusted(IPAddress.Parse(peer)));
    }

    [Fact]
    public void Treat_an_IPv4_mapped_IPv6_peer_as_its_IPv4_address()
    {
        Assert.True(Policy(true, "10.42.0.0/16").IsTrusted(IPAddress.Parse("::ffff:10.42.0.1")));
    }

    [Fact]
    public void Trust_nobody_when_disabled()
    {
        Assert.False(Policy(false, "0.0.0.0/0").IsTrusted(IPAddress.Parse("10.42.0.1")));
    }

    [Fact]
    public void Refuse_a_trusted_network_it_cannot_parse()
    {
        Assert.Throws<FormatException>(() => Policy(true, "10.42.0.0/99"));
    }

    [Fact]
    public void Bind_from_the_environment_variable_names_the_charts_emit()
    {
        // avalon-auth / avalon-world templates render exactly these names.
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Hosting:ProxyProtocol:Enabled"] = "true",
            ["Hosting:ProxyProtocol:HeaderTimeoutSeconds"] = "5",
            ["Hosting:ProxyProtocol:TrustedProxies:0"] = "10.42.0.0/16",
        }).Build();
        HostingConfiguration hosting = new();
        config.GetSection("Hosting").Bind(hosting);

        ProxyProtocolPolicy policy = ProxyProtocolPolicy.From(hosting.ProxyProtocol);

        Assert.True(policy.IsTrusted(IPAddress.Parse("10.42.0.7")));
        Assert.Equal(TimeSpan.FromSeconds(5), policy.HeaderTimeout);
    }
}
