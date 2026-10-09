using System.Net;
using System.Net.Sockets;
using Avalon.Api.Hosting.Config;
using Avalon.Infrastructure.Login;
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

public static class ExemptSourcesExtensions
{
    /// <summary>
    /// The login policy's source for <paramref name="address"/>, exempt from the per-source login budget
    /// (<see cref="SourceBudget"/>) when <paramref name="exempt"/> lists it. Null lists nothing.
    /// </summary>
    public static LoginSource LoginSourceOf(this IExemptSources? exempt, IPAddress address) =>
        LoginSource.FromAddress(address, exempt?.IsExempt(address) == true);
}

/// <summary>
/// <see cref="IExemptSources"/> over the configured entries, parsed once: an IP address (that address only) or a network
/// in CIDR form (address/prefix). An IPv4-mapped IPv6 address, calling or configured, is taken as its IPv4 address.
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

    /// <summary>The refusal for an entry <see cref="TryParse"/> refuses, naming it.</summary>
    public static string Invalid(string? entry) =>
        $"{ApiRateLimiting.Section}:{nameof(RateLimitingConfig.ExemptSources)} has \"{entry}\", which is not an IP " +
        "address or a network in CIDR form (address/prefix) as written: IPv4 in four plain decimal parts, and no " +
        "address bits past the prefix.";

    /// <summary>
    /// Parses one entry, refusing any the parser would read as a different address or network than written: an IPv4
    /// address must be four plain decimal parts (<see cref="IPAddress.TryParse(string?, out IPAddress?)"/> reads "10.1"
    /// as 10.0.0.1, "010.0.0.5" as 8.0.0.5 and accepts hex); a network's address must have no bits past its prefix
    /// (<see cref="IPNetwork.TryParse(string?, out IPNetwork)"/> would quietly clear them, so "10.1.0.5/16" would exempt
    /// all of 10.1.0.0/16). An IPv4-mapped address or network is stored as IPv4, the form callers are matched in; a
    /// mapped network wider than /96 reaches beyond the IPv4 space and is refused.
    /// </summary>
    public static bool TryParse(string? entry, out IPNetwork network)
    {
        network = default;
        if (string.IsNullOrWhiteSpace(entry))
            return false;
        int slash = entry.IndexOf('/', StringComparison.Ordinal);
        string text = slash < 0 ? entry : entry[..slash];
        if (!IPAddress.TryParse(text, out IPAddress? address))
            return false;
        if (address.AddressFamily == AddressFamily.InterNetwork
            && !string.Equals(address.ToString(), text, StringComparison.Ordinal))
        {
            return false;
        }

        int prefix = address.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
        if (slash >= 0)
        {
            if (!IPNetwork.TryParse(entry, out IPNetwork parsed) || !parsed.BaseAddress.Equals(address))
                return false;
            prefix = parsed.PrefixLength;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            if (prefix < 96)
                return false;
            address = address.MapToIPv4();
            prefix -= 96;
        }

        network = new IPNetwork(address, prefix);
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
