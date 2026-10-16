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

    /// <summary>The default <see cref="MaxInFlightPerIPv6Block" />.</summary>
    public const int DefaultMaxInFlightPerIPv6Block = 32;

    /// <summary>The default <see cref="MaxPerSourcePerMinute" />.</summary>
    public const int DefaultMaxPerSourcePerMinute = 120;

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
    /// Admissions one IPv6 /56 may have in flight, across all its /64 sources (#1052): a residential line is commonly
    /// given a /56, 256 /64s, so the per-source cap alone would let one line fill the lane. At least
    /// <see cref="MaxInFlightPerSource" />, at most the lane's backlog. IPv4 has no second level.
    /// </summary>
    [Range(1, WorldDatabaseWork.AdmissionOutstandingLimit)]
    public int MaxInFlightPerIPv6Block { get; set; } = DefaultMaxInFlightPerIPv6Block;

    /// <summary>
    /// Admissions one source may start per minute (#1052), whatever becomes of them, as a steady rate with a burst of the
    /// whole minute's worth: 120 lets a source take 120 at once, then one every half second. Keyed as
    /// <see cref="MaxInFlightPerSource" /> is (an IPv4 address, an IPv6 /64). 1 to 60,000.
    /// </summary>
    [Range(1, 60_000)]
    public int MaxPerSourcePerMinute { get; set; } = DefaultMaxPerSourcePerMinute;

    /// <summary>
    /// Sources no per-source budget counts (named load machines): IP addresses or networks in CIDR form, none wider
    /// than /24 (IPv4) or /64 (IPv6), none covering loopback or a network <c>Hosting:ProxyProtocol:TrustedProxies</c>
    /// lists (<see cref="AdmissionSourceValidation" />). Empty by default.
    /// </summary>
    public List<string> ExemptSources { get; set; } = [];
}

/// <summary>
/// Refuses at startup what annotations cannot: a <c>World:Admission:MaxInFlightPerIPv6Block</c> below
/// <c>MaxInFlightPerSource</c>, and an <c>ExemptSources</c> entry (the first, named, with why) that does not parse as
/// written (<see cref="ExemptNetwork.TryParse" />), is wider than /24 or /64, or covers loopback or a trusted proxy. A
/// trusted proxy's connection that names no client (a PROXY v2 LOCAL header, a health check) keeps the proxy's own
/// address, so exempting the proxy would exempt each of those.
/// </summary>
public sealed class AdmissionSourceValidation(IOptions<HostingConfiguration> hosting) : IValidateOptions<AdmissionSourceOptions>
{
    private const string Setting = AdmissionSourceOptions.Section + ":" + nameof(AdmissionSourceOptions.ExemptSources);

    public ValidateOptionsResult Validate(string? name, AdmissionSourceOptions options)
    {
        if (options.MaxInFlightPerIPv6Block < options.MaxInFlightPerSource)
        {
            return ValidateOptionsResult.Fail(
                $"{AdmissionSourceOptions.Section}:{nameof(AdmissionSourceOptions.MaxInFlightPerIPv6Block)} " +
                $"({options.MaxInFlightPerIPv6Block}) must be at least " +
                $"{AdmissionSourceOptions.Section}:{nameof(AdmissionSourceOptions.MaxInFlightPerSource)} " +
                $"({options.MaxInFlightPerSource}): a /56 holds every /64 in it.");
        }

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

/// <summary>Why <see cref="AdmissionSourceBudget.TryAcquire" /> refused an admission; <see cref="None" /> when it did not.</summary>
public enum AdmissionSourceRefusal
{
    None,

    /// <summary>The source had <c>MaxInFlightPerSource</c> in flight.</summary>
    InFlight,

    /// <summary>The source's IPv6 /56 had <c>MaxInFlightPerIPv6Block</c> in flight.</summary>
    IPv6BlockInFlight,

    /// <summary>The source had started <c>MaxPerSourcePerMinute</c> in the last minute.</summary>
    Rate,
}

/// <summary>
/// Admissions per source at the world's door (#1052): a client that completes the world's TLS and sends a well-formed
/// join ticket, real or not, costs an admission slot and a redemption at the identity service, so one source may hold
/// at most <see cref="AdmissionSourceOptions.MaxInFlightPerSource" /> of them at once and start at most
/// <see cref="AdmissionSourceOptions.MaxPerSourcePerMinute" /> a minute; an IPv6 source's /56 may also hold at most
/// <see cref="AdmissionSourceOptions.MaxInFlightPerIPv6Block" />. One past any of them is refused before its ticket is
/// redeemed. A source is the client's address as the world resolves it (after a trusted proxy's PROXY v2 header,
/// <see cref="Avalon.Hosting.Networking.Connection.ClientIPAddress" />), so it is only as trustworthy as the path to that
/// proxy: an IPv4 address, or an IPv6 /64, which one host is normally given whole and could rotate through, as the login
/// budgets count it. An exempt source (<see cref="AdmissionSourceOptions.ExemptSources" />) is not counted at all.
/// </summary>
/// <remarks>
/// Thread-safe, and never on the tick: slots are taken on the read loops that deliver admissions and given back where
/// the redemption ends, on the thread pool.
/// <para>
/// In flight: exact counts, one entry per source (and per IPv6 /56) with an admission in flight, removed when its last
/// one ends, so the table holds no more entries than admissions in flight, which the admission lane bounds
/// (<see cref="WorldDatabaseWork.AdmissionOutstandingLimit" />), and a refused one gives its slot back at once. A fixed
/// table of striped counters would allocate nothing, but would refuse an honest client whose address shares a stripe
/// with a flooding one.
/// </para>
/// <para>
/// Rate: one timestamp per source, the time its budget is next whole again (a cell-rate check: each admission moves it
/// on by a minute over the rate, and one that would move it more than a minute past now is refused). An entry whose
/// time has passed is the same as none, and is swept away once a minute, or once a second while the table holds
/// <see cref="MaxRateEntries" />; a source first seen while the table is still that full after its sweep is not
/// rate-limited (the in-flight caps still hold), so a flood of new addresses can neither grow the table without bound
/// nor lock new sources out.
/// </para>
/// A source's first admission adds an entry to each table (one node apiece, beside the redemption's own task and
/// request); every other step changes a value in place.
/// </remarks>
public sealed class AdmissionSourceBudget
{
    /// <summary>The most sources the rate table holds; past it, sources it does not hold yet are not rate-limited.</summary>
    public const int MaxRateEntries = 65_536;

    // The key of a connection whose peer is not an IP endpoint: every one of them is one source. No address maps here:
    // an IPv4 key's high half is zero, an IPv6 /64's low half zero and an IPv6 /56's low half one.
    private static readonly UInt128 s_unknownSource = UInt128.MaxValue;

    private readonly ConcurrentDictionary<UInt128, int> _inFlight = new();
    private readonly ConcurrentDictionary<UInt128, long> _rate = new();
    private readonly IPNetwork[] _exempt;
    private readonly TimeProvider _time;

    // The rate in the clock's timestamp units: what one admission moves a source's time on by, and how far past now it
    // may go (a minute less one admission), so a whole minute's worth may be taken at once.
    private readonly long _emission;
    private readonly long _tolerance;
    private readonly long _sweepInterval;
    private readonly long _fullSweepInterval;
    private long _lastSweep;
    private int _rateEntries;

    /// <exception cref="InvalidOperationException">An exempt entry is refused (<see cref="AdmissionSourceValidation" />).</exception>
    public AdmissionSourceBudget(AdmissionSourceOptions options, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxInFlightPerSource, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxInFlightPerIPv6Block, options.MaxInFlightPerSource);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxPerSourcePerMinute, 1);
        MaxInFlightPerSource = options.MaxInFlightPerSource;
        MaxInFlightPerIPv6Block = options.MaxInFlightPerIPv6Block;
        MaxPerSourcePerMinute = options.MaxPerSourcePerMinute;
        _exempt = options.ExemptSources.Select(ParseExempt).ToArray();
        _time = time ?? TimeProvider.System;
        long minute = _time.TimestampFrequency * 60;
        _emission = minute / options.MaxPerSourcePerMinute;
        _tolerance = minute - _emission;
        _sweepInterval = minute;
        _fullSweepInterval = _time.TimestampFrequency;
        _lastSweep = _time.GetTimestamp();
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

    /// <summary><see cref="AdmissionSourceOptions.MaxInFlightPerIPv6Block" />.</summary>
    public int MaxInFlightPerIPv6Block { get; }

    /// <summary><see cref="AdmissionSourceOptions.MaxPerSourcePerMinute" />.</summary>
    public int MaxPerSourcePerMinute { get; }

    /// <summary>
    /// Takes a slot for an admission from <paramref name="address" />, or says why not: its source started
    /// <see cref="MaxPerSourcePerMinute" /> in the last minute (every admission that reaches here spends from that, one
    /// refused for being in flight too), or has <see cref="MaxInFlightPerSource" /> in flight, or its IPv6 /56 has
    /// <see cref="MaxInFlightPerIPv6Block" />. The source's count and its /56's are taken together: when the /56 refuses,
    /// the source's count is given back before this returns. An exempt source always gets a slot, which counts nothing.
    /// See <see cref="Slot" /> for giving it back.
    /// </summary>
    public AdmissionSourceRefusal TryAcquire(IPAddress? address, out Slot slot)
    {
        slot = default;
        if (IsExempt(address))
            return AdmissionSourceRefusal.None;

        UInt128 key = KeyOf(address, out UInt128 block, out bool hasBlock);
        if (!TryTakeRate(key))
            return AdmissionSourceRefusal.Rate;
        if (!TryTake(key, MaxInFlightPerSource))
            return AdmissionSourceRefusal.InFlight;
        if (hasBlock && !TryTake(block, MaxInFlightPerIPv6Block))
        {
            Give(key);
            return AdmissionSourceRefusal.IPv6BlockInFlight;
        }

        slot = new Slot(this, key, block, hasBlock);
        return AdmissionSourceRefusal.None;
    }

    /// <summary>Admissions in flight from <paramref name="address" />'s source; 0 for an exempt one.</summary>
    public int InFlight(IPAddress? address) =>
        !IsExempt(address) && _inFlight.TryGetValue(KeyOf(address, out _, out _), out int held) ? held : 0;

    /// <summary>Admissions in flight from <paramref name="address" />'s IPv6 /56; 0 for IPv4 and an exempt source.</summary>
    public int InFlightInIPv6Block(IPAddress? address)
    {
        if (IsExempt(address))
            return 0;
        _ = KeyOf(address, out UInt128 block, out bool hasBlock);
        return hasBlock && _inFlight.TryGetValue(block, out int held) ? held : 0;
    }

    /// <summary>Sources the rate table holds, swept or not yet: for diagnostics, and to show it does not grow unbounded.</summary>
    public int RateEntries => Volatile.Read(ref _rateEntries);

    /// <summary>
    /// How <paramref name="address" /> is named in a refusal's log line: the source as counted (an IPv4 address, an IPv6
    /// /64), or its /56 for <see cref="AdmissionSourceRefusal.IPv6BlockInFlight" />. Made only when a line is written.
    /// </summary>
    public static string Describe(IPAddress address, AdmissionSourceRefusal refusal)
    {
        if (refusal != AdmissionSourceRefusal.IPv6BlockInFlight || address.AddressFamily != AddressFamily.InterNetworkV6)
            return RemoteAddress.SourceOf(address);
        byte[] bytes = address.GetAddressBytes();
        Array.Clear(bytes, 7, 9);
        return $"{new IPAddress(bytes)}/56";
    }

    private bool TryTake(UInt128 key, int max)
    {
        while (true)
        {
            if (_inFlight.TryGetValue(key, out int held))
            {
                if (held >= max)
                    return false;
                if (_inFlight.TryUpdate(key, held + 1, held))
                    return true;
            }
            else if (_inFlight.TryAdd(key, 1))
            {
                return true;
            }
        }
    }

    private void Give(UInt128 key)
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

    private bool TryTakeRate(UInt128 key)
    {
        long now = _time.GetTimestamp();
        SweepIfDue(now);
        while (true)
        {
            if (_rate.TryGetValue(key, out long whole))
            {
                long from = Math.Max(whole, now);
                if (from - now > _tolerance)
                    return false;
                if (_rate.TryUpdate(key, from + _emission, whole))
                    return true;
            }
            else
            {
                if (Interlocked.Increment(ref _rateEntries) > MaxRateEntries)
                {
                    // Full after its sweep: not tracked, and not refused for it (the in-flight caps still hold).
                    Interlocked.Decrement(ref _rateEntries);
                    return true;
                }

                if (_rate.TryAdd(key, now + _emission))
                    return true;
                Interlocked.Decrement(ref _rateEntries);
            }
        }
    }

    /// <summary>
    /// Drops the sources whose budget is whole again, which are the same as none: once a minute, or once a second while
    /// the table is full. One thread sweeps; the rest go on. Removal compares the value, so a source taken from since
    /// the sweep read it stays.
    /// </summary>
    private void SweepIfDue(long now)
    {
        long last = Volatile.Read(ref _lastSweep);
        long due = Volatile.Read(ref _rateEntries) >= MaxRateEntries ? _fullSweepInterval : _sweepInterval;
        if (now - last < due || Interlocked.CompareExchange(ref _lastSweep, now, last) != last)
            return;
        foreach (KeyValuePair<UInt128, long> entry in _rate)
        {
            if (entry.Value <= now && _rate.TryRemove(entry))
                Interlocked.Decrement(ref _rateEntries);
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
    /// whose high half is zero; an IPv6 address as its /64, whose low half is zero, and its /56 as
    /// <paramref name="block" />, whose low half is one.
    /// </summary>
    private static UInt128 KeyOf(IPAddress? address, out UInt128 block, out bool hasBlock)
    {
        block = default;
        hasBlock = false;
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
        if (high == 0 && low >> 32 == 0xFFFF)
            return new UInt128(0, low);
        block = new UInt128(high & ~0xFFUL, 1);
        hasBlock = true;
        return new UInt128(high, 0);
    }

    /// <summary>
    /// One admission's slot, from <see cref="TryAcquire" />: its source's count and, for IPv6, its /56's. The holder must
    /// call <see cref="Release" /> exactly once, where the admission's redemption has ended or where it was refused
    /// before it started, and never on a copy that was released already: a value has no way to know, and a second
    /// release would give back a slot another admission from the same source holds. The default slot (an exempt
    /// source's, or a refusal's) releases nothing.
    /// </summary>
    public readonly struct Slot
    {
        private readonly AdmissionSourceBudget? _budget;
        private readonly UInt128 _key;
        private readonly UInt128 _block;
        private readonly bool _hasBlock;

        internal Slot(AdmissionSourceBudget budget, UInt128 key, UInt128 block, bool hasBlock)
        {
            _budget = budget;
            _key = key;
            _block = block;
            _hasBlock = hasBlock;
        }

        public void Release()
        {
            if (_budget is null)
                return;
            _budget.Give(_key);
            if (_hasBlock)
                _budget.Give(_block);
        }
    }
}
