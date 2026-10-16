using Avalon.Configuration;
using Avalon.World.GameAuth;
using Microsoft.Extensions.Options;

namespace Avalon.Server.World.UnitTests.GameAuth;

/// <summary>
/// <c>World:Admission:ExemptSources</c> is checked before the world listens (#1052). Entries are read as the REST API's
/// exempt sources are (the shared parser's cases are the API's startup test); what the world adds is the proxies it
/// trusts for PROXY v2 headers: a connection of theirs that names no client keeps the proxy's own address, so an entry
/// covering one, as the k3s pod network 10.42.0.0/16 is trusted on the homelab, is refused, as is one wider than /24.
/// </summary>
public sealed class AdmissionSourceValidationShould
{
    [Theory]
    [InlineData("10.10.1.21/32", null)]
    [InlineData("10.42.3.0/24", "\"10.42.3.0/24\", which covers a proxy Hosting:ProxyProtocol:TrustedProxies trusts")]
    [InlineData("10.10.0.0/16", "\"10.10.0.0/16\", wider than /24")]
    public void Refuse_an_exempt_source_wider_than_a_few_machines_or_covering_a_trusted_proxy(string entry, string? refusalNaming)
    {
        var hosting = new HostingConfiguration();
        hosting.ProxyProtocol.TrustedProxies.Add("10.42.0.0/16");
        ValidateOptionsResult result = new AdmissionSourceValidation(Options.Create(hosting))
            .Validate(null, new AdmissionSourceOptions { ExemptSources = [entry] });

        if (refusalNaming is null)
        {
            Assert.True(result.Succeeded);
            return;
        }

        Assert.True(result.Failed);
        Assert.Contains($"World:Admission:ExemptSources has {refusalNaming}", result.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>A /56 holds every /64 in it, so its cap below a source's would refuse a source under its own.</summary>
    [Fact]
    public void Refuse_an_IPv6_block_cap_below_the_source_cap()
    {
        ValidateOptionsResult result = new AdmissionSourceValidation(Options.Create(new HostingConfiguration()))
            .Validate(null, new AdmissionSourceOptions { MaxInFlightPerSource = 16, MaxInFlightPerIPv6Block = 8 });

        Assert.True(result.Failed);
        Assert.Contains("World:Admission:MaxInFlightPerIPv6Block (8) must be at least World:Admission:MaxInFlightPerSource (16)",
            result.FailureMessage, StringComparison.Ordinal);
    }
}
