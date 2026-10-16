using System.Net;
using Avalon.Api.Hosting.Config;
using Avalon.Infrastructure.Login;
using Microsoft.Extensions.Options;
using ForwardedHeadersOptions = Microsoft.AspNetCore.Builder.ForwardedHeadersOptions;
using IPNetwork = System.Net.IPNetwork;

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
/// An entry must name a few load machines, never a range of callers: none wider than /<see cref="ShortestIPv4Prefix"/>
/// (IPv4) or /<see cref="ShortestIPv6Prefix"/> (IPv6), and none covering loopback; startup also refuses one covering a
/// trusted proxy (<see cref="RefusalOf"/>).
/// </summary>
public sealed class ExemptSources : IExemptSources
{
    /// <inheritdoc cref="ExemptNetwork.ShortestIPv4Prefix"/>
    public const int ShortestIPv4Prefix = ExemptNetwork.ShortestIPv4Prefix;

    /// <inheritdoc cref="ExemptNetwork.ShortestIPv4Prefix"/>
    public const int ShortestIPv6Prefix = ExemptNetwork.ShortestIPv6Prefix;

    private readonly IPNetwork[] _networks;

    /// <summary>Throws, naming the entry and the reason, for an entry <see cref="RefusalOf"/> refuses without proxies.</summary>
    public ExemptSources(IEnumerable<string> entries) =>
        _networks = entries.Select(entry => RefusalOf(entry, null) is { } refusal
            ? throw new InvalidOperationException(refusal)
            : Parse(entry)).ToArray();

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

    /// <summary>
    /// Why <paramref name="entry"/> is refused, naming it, or null when it is accepted: it does not parse as written
    /// (<see cref="TryParse"/>), it is wider than the widest exempt network, or it overlaps loopback or a proxy
    /// <paramref name="proxies"/> trusts (<c>Application:ForwardedHeaders</c>). A trusted proxy forwards other callers,
    /// so exempting its address would exempt every caller whose header it does not rewrite, and every request it makes
    /// with none.
    /// </summary>
    public static string? RefusalOf(string? entry, ForwardedHeadersOptions? proxies)
    {
        if (!TryParse(entry, out IPNetwork network))
            return Invalid(entry);

        if (ExemptNetwork.IsTooWide(network))
        {
            int shortest = ExemptNetwork.ShortestPrefixOf(network);
            return $"{Setting} has \"{entry}\", wider than /{shortest}: an exempt network is a hole in every per-source " +
                   "limit, so list the load machines' own addresses or a network no wider than that.";
        }

        if (ExemptNetwork.CoversLoopback(network))
            return $"{Setting} has \"{entry}\", which covers loopback, a trusted proxy: every caller it forwards would be exempt.";

        if (proxies is not null
            && (proxies.KnownProxies.Any(proxy => network.Contains(Unmap(proxy)))
                || proxies.KnownIPNetworks.Any(trusted => ExemptNetwork.Overlap(network, trusted))))
        {
            return $"{Setting} has \"{entry}\", which covers a proxy {ForwardedHeadersSetup.Section} trusts: every " +
                   "caller it forwards would be exempt.";
        }

        return null;
    }

    private static string Setting => $"{ApiRateLimiting.Section}:{nameof(RateLimitingConfig.ExemptSources)}";

    private static IPNetwork Parse(string entry) =>
        TryParse(entry, out IPNetwork network) ? network : throw new InvalidOperationException(Invalid(entry));

    private static IPAddress Unmap(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    /// <summary>The refusal for an entry <see cref="TryParse"/> refuses, naming it.</summary>
    public static string Invalid(string? entry) =>
        $"{ApiRateLimiting.Section}:{nameof(RateLimitingConfig.ExemptSources)} has \"{entry}\", which is not an IP " +
        "address or a network in CIDR form (address/prefix) as written: IPv4 in four plain decimal parts, and no " +
        "address bits past the prefix.";

    /// <inheritdoc cref="ExemptNetwork.TryParse"/>
    public static bool TryParse(string? entry, out IPNetwork network) => ExemptNetwork.TryParse(entry, out network);
}

/// <summary>
/// Refuses <c>Application:RateLimiting:ExemptSources</c> at startup, naming the first entry refused and why
/// (<see cref="ExemptSources.RefusalOf"/>), against the proxies the forwarded-headers middleware trusts.
/// </summary>
/// <param name="proxies">The registered forwarded-headers options; null (no hosting registered) checks loopback only.</param>
internal sealed class ExemptSourcesValidation(ForwardedHeadersOptions? proxies = null) : IValidateOptions<RateLimitingConfig>
{
    public ValidateOptionsResult Validate(string? name, RateLimitingConfig options)
    {
        foreach (string entry in options.ExemptSources)
        {
            if (ExemptSources.RefusalOf(entry, proxies) is { } refusal)
                return ValidateOptionsResult.Fail(refusal);
        }

        return ValidateOptionsResult.Success;
    }
}
