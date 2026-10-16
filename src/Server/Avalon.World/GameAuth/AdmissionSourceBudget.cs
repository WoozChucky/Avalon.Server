using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Sockets;
using Avalon.Configuration;
using Avalon.Infrastructure.Login;
using Avalon.World.Persistence;
using Microsoft.Extensions.Options;

namespace Avalon.World.GameAuth;

/// <summary>
/// The per-source budget on admissions at the world's door (#1052), under <c>World:Admission</c> beside
/// <see cref="GameAdmissionOptions" />. Read once at startup.
/// </summary>
public sealed class AdmissionSourceOptions
{
    public const string Section = GameAdmissionOptions.Section;

    /// <summary>The default <see cref="MaxInFlightPerSource" />.</summary>
    public const int DefaultMaxInFlightPerSource = 16;

    /// <summary>
    /// Admissions one source may have in flight, from the moment the world takes one to redeem until its redemption has
    /// ended (#1052). One past it is answered <c>ServiceUnavailable</c> and closed before its ticket is redeemed. A
    /// client has at most one in flight, so 16 leaves a household behind one address room to spare. At most the
    /// admission lane's whole backlog (<see cref="WorldDatabaseWork.AdmissionOutstandingLimit" />), which no one
    /// source can pass anyway; it cannot be switched off.
    /// </summary>
    [Range(1, WorldDatabaseWork.AdmissionOutstandingLimit)]
    public int MaxInFlightPerSource { get; set; } = DefaultMaxInFlightPerSource;

    /// <summary>
    /// Sources no per-source budget counts (named load machines): IP addresses or networks in CIDR form, none wider
    /// than /24 (IPv4) or /64 (IPv6), none covering loopback or a network <c>Hosting:ProxyProtocol:TrustedProxies</c>
    /// lists (<see cref="AdmissionSourceValidation" />). Empty by default.
    /// </summary>
    public List<string> ExemptSources { get; set; } = [];
}

/// <summary>
/// Refuses <c>World:Admission:ExemptSources</c> at startup, naming the first entry refused and why: it does not parse as
/// written (<see cref="ExemptNetwork.TryParse" />), it is wider than /24 or /64, or it covers loopback or a trusted
/// proxy. A trusted proxy's connection that names no client (a PROXY v2 LOCAL header, a health check) keeps the
/// proxy's own address, so exempting the proxy would exempt each of those.
/// </summary>
public sealed class AdmissionSourceValidation(IOptions<HostingConfiguration> hosting) : IValidateOptions<AdmissionSourceOptions>
{
    private const string Setting = AdmissionSourceOptions.Section + ":" + nameof(AdmissionSourceOptions.ExemptSources);

    public ValidateOptionsResult Validate(string? name, AdmissionSourceOptions options)
    {
        foreach (string entry in options.ExemptSources)
        {
            if (RefusalOf(entry, hosting.Value.ProxyProtocol.TrustedProxies) is { } refusal)
                return ValidateOptionsResult.Fail(refusal);
        }

        return ValidateOptionsResult.Success;
    }

    /// <summary>Why <paramref name="entry" /> is refused, naming it, or null when it is accepted.</summary>
    public static string? RefusalOf(string? entry, IEnumerable<string> trustedProxies)
    {
        if (!ExemptNetwork.TryParse(entry, out IPNetwork network))
        {
            return $"{Setting} has \"{entry}\", which is not an IP address or a network in CIDR form (address/prefix) as " +
                   "written: IPv4 in four plain decimal parts, and no address bits past the prefix.";
        }

        if (ExemptNetwork.IsTooWide(network))
        {
            return $"{Setting} has \"{entry}\", wider than /{ExemptNetwork.ShortestPrefixOf(network)}: an exempt network " +
                   "is a hole in the per-source budget, so list the load machines' own addresses or a network no wider than that.";
        }

        if (ExemptNetwork.CoversLoopback(network))
            return $"{Setting} has \"{entry}\", which covers loopback.";

        // An entry of TrustedProxies that is not CIDR stops the start where the TCP server is built.
        foreach (string proxy in trustedProxies)
        {
            if (IPNetwork.TryParse(proxy, out IPNetwork trusted) && ExemptNetwork.Overlap(network, trusted))
            {
                return $"{Setting} has \"{entry}\", which covers a proxy Hosting:ProxyProtocol:TrustedProxies trusts: " +
                       "every connection of its that names no client would be exempt.";
            }
        }

        return null;
    }
}

/// <summary>
/// Admissions in flight per source at the world's door (#1052): a client that completes the world's TLS and sends a
/// well-formed join ticket, real or not, costs an admission slot and a redemption at the identity service, so one
/// source may hold at most <see cref="AdmissionSourceOptions.MaxInFlightPerSource" /> of them at once, and one past
/// that is refused before its ticket is redeemed. A source is the client's address as the world resolves it (after a
/// trusted proxy's PROXY v2 header, <see cref="Avalon.Hosting.Networking.Connection.ClientIPAddress" />): an IPv4 address, or
/// an IPv6 /64, which one host is normally given whole and could rotate through, as the login budgets count it. An
/// exempt source (<see cref="AdmissionSourceOptions.ExemptSources" />) is not counted.
/// </summary>
/// <remarks>
/// Thread-safe, and never on the tick: slots are taken on the read loops that deliver admissions and given back where
/// the redemption ends, on the thread pool. The count is exact, one entry per source with an admission in flight,
/// removed when its last one ends: the table holds no more entries than admissions in flight, which the admission
/// lane bounds (<see cref="WorldDatabaseWork.AdmissionOutstandingLimit" />), and a refused one gives its slot back at
/// once. A source's first admission adds an entry (one node, beside the redemption's own task and request); every
/// other step changes a count in place. A fixed table of striped counters would allocate nothing, but would refuse an
/// honest client whose address shares a stripe with a flooding one.
/// </remarks>
public sealed class AdmissionSourceBudget
{
    // The key of a connection whose peer is not an IP endpoint: every one of them is one source. No address maps here:
    // an IPv4 key has its high half zero, an IPv6 /64 its low half.
    private static readonly UInt128 s_unknownSource = UInt128.MaxValue;

    private readonly ConcurrentDictionary<UInt128, int> _inFlight = new();
    private readonly IPNetwork[] _exempt;

    /// <exception cref="InvalidOperationException">An exempt entry is refused (<see cref="AdmissionSourceValidation" />).</exception>
    public AdmissionSourceBudget(AdmissionSourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxInFlightPerSource, 1);
        MaxInFlightPerSource = options.MaxInFlightPerSource;
        _exempt = options.ExemptSources.Select(ParseExempt).ToArray();
    }

    private static IPNetwork ParseExempt(string entry)
    {
        if (AdmissionSourceValidation.RefusalOf(entry, []) is { } refusal)
            throw new InvalidOperationException(refusal);
        _ = ExemptNetwork.TryParse(entry, out IPNetwork network);
        return network;
    }

    /// <summary><see cref="AdmissionSourceOptions.MaxInFlightPerSource" />.</summary>
    public int MaxInFlightPerSource { get; }

    /// <summary>
    /// Takes a slot for an admission from <paramref name="address" />, or refuses it: false when its source already has
    /// <see cref="MaxInFlightPerSource" /> in flight. An exempt source always gets one, which counts nothing. The slot
    /// is given back once, by <see cref="Slot.Release" />, when the admission's redemption has ended or was never
    /// started.
    /// </summary>
    public bool TryAcquire(IPAddress? address, out Slot slot)
    {
        if (IsExempt(address))
        {
            slot = default;
            return true;
        }

        UInt128 key = KeyOf(address);
        while (true)
        {
            if (_inFlight.TryGetValue(key, out int held))
            {
                if (held >= MaxInFlightPerSource)
                {
                    slot = default;
                    return false;
                }

                if (_inFlight.TryUpdate(key, held + 1, held))
                    break;
            }
            else if (_inFlight.TryAdd(key, 1))
            {
                break;
            }
        }

        slot = new Slot(this, key);
        return true;
    }

    /// <summary>Admissions in flight from <paramref name="address" />'s source; 0 for an exempt one.</summary>
    public int InFlight(IPAddress? address) =>
        !IsExempt(address) && _inFlight.TryGetValue(KeyOf(address), out int held) ? held : 0;

    private void Release(UInt128 key)
    {
        // The last one out removes the entry, but only while it still reads 1: a slot taken in between makes the
        // removal fail, and the count is read again.
        while (_inFlight.TryGetValue(key, out int held))
        {
            if (held <= 1
                    ? _inFlight.TryRemove(new KeyValuePair<UInt128, int>(key, held))
                    : _inFlight.TryUpdate(key, held - 1, held))
            {
                return;
            }
        }
    }

    private bool IsExempt(IPAddress? address)
    {
        if (address is null || _exempt.Length == 0)
            return false;
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        foreach (IPNetwork network in _exempt)
        {
            if (network.Contains(address))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The source's key, without allocating: an IPv4 address (or an IPv4-mapped IPv6 one) as its IPv4-mapped value,
    /// whose high half is zero; an IPv6 address as its /64, whose low half is zero.
    /// </summary>
    private static UInt128 KeyOf(IPAddress? address)
    {
        if (address is null)
            return s_unknownSource;
        Span<byte> bytes = stackalloc byte[16];
        if (!address.TryWriteBytes(bytes, out int written))
            return s_unknownSource;
        if (address.AddressFamily == AddressFamily.InterNetwork && written == 4)
            return new UInt128(0, 0xFFFF_0000_0000UL | BinaryPrimitives.ReadUInt32BigEndian(bytes));
        if (written != 16)
            return s_unknownSource;
        ulong high = BinaryPrimitives.ReadUInt64BigEndian(bytes);
        ulong low = BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]);
        // ::ffff:a.b.c.d is the IPv4 address a.b.c.d.
        return high == 0 && low >> 32 == 0xFFFF ? new UInt128(0, low) : new UInt128(high, 0);
    }

    /// <summary>
    /// One admission's slot, from <see cref="TryAcquire" />. Released once, where the admission's redemption has ended
    /// or was refused before it started; the default slot (an exempt source's) releases nothing.
    /// </summary>
    public readonly struct Slot
    {
        private readonly AdmissionSourceBudget? _budget;
        private readonly UInt128 _key;

        internal Slot(AdmissionSourceBudget budget, UInt128 key)
        {
            _budget = budget;
            _key = key;
        }

        public void Release() => _budget?.Release(_key);
    }
}
