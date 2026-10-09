using System.Net;
using Avalon.Api.Hosting.Middlewares;
using Xunit;

namespace Avalon.Api.Hosting.UnitTests.Middlewares;

/// <summary>
/// The sources exempt from the per-source limits: a plain address is that address only, a network every address in it,
/// and a caller reaching an IPv4 listener through IPv6 (an IPv4-mapped address) is its IPv4 address, as is a network
/// configured in that form.
/// </summary>
public class ExemptSourcesShould
{
    private static readonly ExemptSources s_exempt = new(["10.0.0.5", "10.1.0.0/16", "::1", "2001:db8::/64", "::ffff:192.168.0.0/112"]);

    [Theory]
    [InlineData("10.0.0.5", true)]
    [InlineData("10.0.0.6", false)]
    [InlineData("10.1.200.3", true)]
    [InlineData("10.2.0.1", false)]
    [InlineData("::ffff:10.1.2.3", true)]
    [InlineData("::ffff:10.0.0.5", true)]
    [InlineData("::1", true)]
    [InlineData("::2", false)]
    [InlineData("2001:db8::abcd", true)]
    [InlineData("2001:db8:0:1::1", false)]
    [InlineData("192.168.4.5", true)]
    [InlineData("::ffff:192.168.4.5", true)]
    [InlineData("192.169.0.1", false)]
    [InlineData(null, false)]
    public void Exempt_only_the_listed_addresses_and_networks(string? address, bool exempt) =>
        Assert.Equal(exempt, s_exempt.IsExempt(address is null ? null : IPAddress.Parse(address)));
}
