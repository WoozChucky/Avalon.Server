using System.Net;
using System.Net.Sockets;
using Avalon.Api.Hosting.Config;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Hosting.Middlewares;

/// <summary>
/// The sources <c>Application:RateLimiting:ExemptSources</c> names (named load machines), which skip the per-source
/// limits: the anonymous and client-auth request partitions and the per-source login and account-creation budgets.
/// Per-account and per-username limits still apply to them.
/// </summary>
public interface IExemptSources
{
    /// <summary>
    /// Whether <paramref name="address"/>, the caller's address after the forwarded-headers middleware, is exempt.
    /// No address is never exempt.
    /// </summary>
    bool IsExempt(IPAddress? address);
}

/// <summary>
/// <see cref="IExemptSources"/> over the configured entries, parsed once: an IP address (that address only) or a network
/// in CIDR form (address/prefix). An IPv4-mapped IPv6 address, calling or configured as a plain address, is taken as
/// its IPv4 address.
/// </summary>
public sealed class ExemptSources : IExemptSources
{
    private readonly IPNetwork[] _networks;

    public ExemptSources(IEnumerable<string> entries) =>
        _networks = entries.Select(entry => TryParse(entry, out IPNetwork network)
            ? network
            : throw new InvalidOperationException(Invalid(entry))).ToArray();

    public bool IsExempt(IPAddress? address)
    {
        if (address is null || _networks.Length == 0)
            return false;
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        foreach (IPNetwork network in _networks)
        {
            if (network.Contains(address))
                return true;
        }

        return false;
    }

    /// <summary>The refusal for an entry that is neither an address nor a network, naming it.</summary>
    public static string Invalid(string? entry) =>
        $"{ApiRateLimiting.Section}:{nameof(RateLimitingConfig.ExemptSources)} has \"{entry}\", which is neither an " +
        "IP address nor a network in CIDR form (address/prefix).";

    /// <summary>
    /// Parses one entry. An IPv4 address must have its four parts: <see cref="IPAddress.TryParse(string?, out IPAddress?)"/>
    /// would read "10.1" as 10.0.0.1. A network's address must have no bits past its prefix.
    /// </summary>
    public static bool TryParse(string? entry, out IPNetwork network)
    {
        network = default;
        if (string.IsNullOrWhiteSpace(entry))
            return false;
        int slash = entry.IndexOf('/', StringComparison.Ordinal);
        string address = slash < 0 ? entry : entry[..slash];
        if (!address.Contains(':', StringComparison.Ordinal) && address.Count(c => c == '.') != 3)
            return false;
        if (slash >= 0)
            return IPNetwork.TryParse(entry, out network);
        if (!IPAddress.TryParse(entry, out IPAddress? parsed))
            return false;
        if (parsed.IsIPv4MappedToIPv6)
            parsed = parsed.MapToIPv4();
        network = new IPNetwork(parsed, parsed.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32);
        return true;
    }
}

/// <summary>
/// Refuses <c>Application:RateLimiting:ExemptSources</c> at startup, naming the first entry that does not parse
/// (<see cref="ExemptSources.TryParse"/>).
/// </summary>
internal sealed class ExemptSourcesValidation : IValidateOptions<RateLimitingConfig>
{
    public ValidateOptionsResult Validate(string? name, RateLimitingConfig options)
    {
        foreach (string entry in options.ExemptSources)
        {
            if (!ExemptSources.TryParse(entry, out _))
                return ValidateOptionsResult.Fail(ExemptSources.Invalid(entry));
        }

        return ValidateOptionsResult.Success;
    }
}
