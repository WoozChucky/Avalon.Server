using System.Net;
using System.Net.Sockets;

namespace Avalon.Infrastructure.Login;

/// <summary>
/// One entry of a list of sources exempt from a per-source limit (named load machines): an IP address (that address
/// only) or a network in CIDR form (address/prefix), read strictly and held to the size of a few machines. The REST
/// API's <c>Application:RateLimiting:ExemptSources</c> and the world's <c>World:Admission:ExemptSources</c> (#1052) read
/// their entries here, so both refuse the same ones; each names its own setting in the refusal.
/// </summary>
public static class ExemptNetwork
{
    /// <summary>
    /// The widest exempt network for IPv4, /24, and for IPv6, /64 (one host's usual allocation, and the source the
    /// login budgets count): an exempt range is a hole in every per-source limit, so it stays the size of a few machines.
    /// </summary>
    public const int ShortestIPv4Prefix = 24;

    /// <inheritdoc cref="ShortestIPv4Prefix"/>
    public const int ShortestIPv6Prefix = 64;

    private static readonly IPNetwork[] s_loopback = [new(IPAddress.Loopback, 8), new(IPAddress.IPv6Loopback, 128)];

    /// <summary>The widest prefix an exempt network of <paramref name="network"/>'s family may have.</summary>
    public static int ShortestPrefixOf(IPNetwork network) =>
        network.BaseAddress.AddressFamily == AddressFamily.InterNetworkV6 ? ShortestIPv6Prefix : ShortestIPv4Prefix;

    /// <summary>Whether <paramref name="network"/> is wider than an exempt network may be (<see cref="ShortestIPv4Prefix"/>).</summary>
    public static bool IsTooWide(IPNetwork network) => network.PrefixLength < ShortestPrefixOf(network);

    /// <summary>Whether <paramref name="network"/> shares an address with loopback, which a local proxy speaks from.</summary>
    public static bool CoversLoopback(IPNetwork network) => s_loopback.Any(loopback => Overlap(network, loopback));

    /// <summary>
    /// Whether two networks share an address: one holds the other's base address. An IPv4-mapped <paramref name="other"/>
    /// of /96 or narrower is taken as its IPv4 network first.
    /// </summary>
    public static bool Overlap(IPNetwork exempt, IPNetwork other)
    {
        IPNetwork trusted = other.BaseAddress.IsIPv4MappedToIPv6 && other.PrefixLength >= 96
            ? new IPNetwork(other.BaseAddress.MapToIPv4(), other.PrefixLength - 96)
            : other;
        return exempt.BaseAddress.AddressFamily == trusted.BaseAddress.AddressFamily
               && (exempt.Contains(trusted.BaseAddress) || trusted.Contains(exempt.BaseAddress));
    }

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
