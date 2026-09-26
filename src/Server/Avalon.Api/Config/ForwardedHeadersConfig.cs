namespace Avalon.Api.Config;

/// <summary>
/// The reverse proxies whose <c>X-Forwarded-For</c> the api believes (#478 review), bound from
/// <c>Application:ForwardedHeaders</c>. The caller's address decides its login source budget, so
/// a proxy that is not trusted makes every caller behind it one source, while trusting a header
/// from anyone lets a caller pick its own source. Loopback is always trusted; nothing else is
/// unless it is listed here. A network wider than /8 (IPv4) or /32 (IPv6), <c>0.0.0.0/0</c> and <c>::/0</c> included, is
/// refused at startup.
/// </summary>
public class ForwardedHeadersConfig
{
    /// <summary>Addresses of trusted proxies, such as <c>10.0.0.2</c>.</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>Networks of trusted proxies in CIDR form, such as <c>10.0.0.0/8</c> for a cluster's pod network.</summary>
    public string[] KnownNetworks { get; set; } = [];

    /// <summary>How many proxy hops of <c>X-Forwarded-For</c> are read, from the right. At least 1.</summary>
    public int ForwardLimit { get; set; } = 1;
}
