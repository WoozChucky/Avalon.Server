using System.ComponentModel.DataAnnotations;

namespace Avalon.Configuration;

/// <summary>
/// PROXY protocol v2 on the TCP servers (Hosting:ProxyProtocol). An L4 proxy in front of the server
/// (e.g. Caddy's layer4 app) prefixes each connection with the real client address; without it every
/// player shares the proxy's address, and per-source limits apply to all of them together (#524).
/// </summary>
public class ProxyProtocolConfiguration
{
    /// <summary>Off by default: a server nobody fronts reads no header.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Networks (CIDR, e.g. <c>10.42.0.0/16</c>) whose connections must start with a PROXY v2 header.
    /// Only these peers may assert a client address; a trusted peer that sends no valid header is
    /// dropped. Connections from any other address are direct clients and are read as-is.
    /// </summary>
    public List<string> TrustedProxies { get; set; } = [];

    /// <summary>How long a trusted peer has to send the header before the connection is dropped.</summary>
    [Range(1, 60)]
    public int HeaderTimeoutSeconds { get; set; } = 5;
}
