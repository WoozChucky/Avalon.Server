// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Net;
using Avalon.Configuration;

namespace Avalon.Hosting.Networking;

/// <summary>
/// Which peers must (and may) prefix their connection with a PROXY v2 header. Built once from
/// <see cref="ProxyProtocolConfiguration" />; an invalid network fails at startup, not per connection.
/// </summary>
public sealed class ProxyProtocolPolicy
{
    public static readonly ProxyProtocolPolicy Disabled = new(false, [], TimeSpan.FromSeconds(5));

    private readonly IPNetwork[] _trusted;

    private ProxyProtocolPolicy(bool enabled, IPNetwork[] trusted, TimeSpan headerTimeout)
    {
        Enabled = enabled;
        _trusted = trusted;
        HeaderTimeout = headerTimeout;
    }

    public bool Enabled { get; }

    public TimeSpan HeaderTimeout { get; }

    /// <exception cref="FormatException">A trusted network is not valid CIDR.</exception>
    public static ProxyProtocolPolicy From(ProxyProtocolConfiguration config) =>
        new(config.Enabled,
            config.TrustedProxies.Select(IPNetwork.Parse).ToArray(),
            TimeSpan.FromSeconds(config.HeaderTimeoutSeconds));

    /// <summary>Whether <paramref name="peer" /> is a proxy whose connections carry a PROXY header.</summary>
    public bool IsTrusted(IPAddress peer)
    {
        if (!Enabled)
            return false;

        IPAddress address = peer.IsIPv4MappedToIPv6 ? peer.MapToIPv4() : peer;
        return _trusted.Any(network => network.Contains(address));
    }
}
