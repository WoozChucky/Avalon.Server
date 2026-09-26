using System.Net;
using Avalon.Api.Config;
using Microsoft.AspNetCore.HttpOverrides;

namespace Avalon.Api.Middlewares;

/// <summary>
/// Which proxies' <c>X-Forwarded-For</c> the api believes (#478 review). The caller's address is its
/// login source (the per-source budget), so both mistakes are costly: a proxy left untrusted makes
/// every caller behind it one source, and one budget for everyone; a header believed from anyone
/// lets a caller choose its source and escape the budget. Loopback is trusted by default, as
/// ASP.NET Core's defaults have it; anything else must be configured under
/// <see cref="Section"/>, and a network that trusts every address is refused.
/// </summary>
public static class ForwardedHeadersSetup
{
    public const string Section = "Application:ForwardedHeaders";

    /// <summary>
    /// The options for <c>UseForwardedHeaders</c>: loopback plus the configured proxies and
    /// networks, and the configured hop limit. Throws, naming the setting, for an entry that does not
    /// parse, a network with prefix length 0, or a limit below 1.
    /// </summary>
    public static ForwardedHeadersOptions BuildOptions(ForwardedHeadersConfig? config)
    {
        config ??= new ForwardedHeadersConfig();
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        };

        if (config.ForwardLimit < 1)
            throw new InvalidOperationException($"{Section}:ForwardLimit must be at least 1 (it is {config.ForwardLimit}).");
        options.ForwardLimit = config.ForwardLimit;

        foreach (string entry in config.KnownProxies)
        {
            if (!IPAddress.TryParse(entry, out IPAddress? address))
                throw new InvalidOperationException($"{Section}:KnownProxies has \"{entry}\", which is not an IP address.");
            options.KnownProxies.Add(address);
        }

        foreach (string entry in config.KnownNetworks)
        {
            if (!entry.Contains('/', StringComparison.Ordinal) || !System.Net.IPNetwork.TryParse(entry, out System.Net.IPNetwork network))
                throw new InvalidOperationException(
                    $"{Section}:KnownNetworks has \"{entry}\", which is not a network in CIDR form (address/prefix).");
            if (network.PrefixLength == 0)
                throw new InvalidOperationException(
                    $"{Section}:KnownNetworks has \"{entry}\", which trusts every address: any caller could choose its own source.");
            options.KnownIPNetworks.Add(network);
        }

        return options;
    }

    /// <summary>Whether any proxy beyond loopback is trusted.</summary>
    public static bool TrustsAnyProxy(ForwardedHeadersConfig? config) =>
        config is not null && (config.KnownProxies.Length > 0 || config.KnownNetworks.Length > 0);

    /// <summary>
    /// Outside Development, warns when no proxy beyond loopback is trusted: behind an ingress that
    /// leaves every REST caller one login source.
    /// </summary>
    public static void WarnIfNoProxyTrusted(ILogger logger, ForwardedHeadersConfig? config, IHostEnvironment environment)
    {
        if (environment.IsDevelopment() || TrustsAnyProxy(config))
            return;

        logger.LogWarning(
            "No proxy is trusted for X-Forwarded-For beyond loopback ({Section}:KnownProxies and KnownNetworks are empty). " +
            "Behind an ingress every REST caller then has the ingress's address and shares one login source budget",
            Section);
    }

    /// <summary>
    /// <c>UseForwardedHeaders</c> with the registered <see cref="ForwardedHeadersOptions"/>, after
    /// a check that logs a header sent by a peer that is not trusted.
    /// </summary>
    public static IApplicationBuilder UseAvalonForwardedHeaders(this IApplicationBuilder app)
    {
        UntrustedForwardedHeaderLog log = app.ApplicationServices.GetRequiredService<UntrustedForwardedHeaderLog>();
        app.Use((context, next) =>
        {
            log.Observe(context);
            return next(context);
        });
        return app.UseForwardedHeaders(app.ApplicationServices.GetRequiredService<ForwardedHeadersOptions>());
    }
}

/// <summary>
/// Logs a request that carries <c>X-Forwarded-For</c> from a peer that is not a trusted proxy, at
/// most once per <see cref="Interval"/>, with how many were not logged since. The header is ignored
/// then, so either a proxy is missing from the configuration or a caller is trying to choose its
/// own source; both are worth seeing, neither worth a line per request.
/// </summary>
public sealed class UntrustedForwardedHeaderLog
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly ForwardedHeadersOptions _options;
    private long _nextLogTicks = long.MinValue;
    private long _suppressed;

    public UntrustedForwardedHeaderLog(ILoggerFactory loggerFactory, TimeProvider time, ForwardedHeadersOptions options)
    {
        _logger = loggerFactory.CreateLogger<UntrustedForwardedHeaderLog>();
        _time = time;
        _options = options;
    }

    public void Observe(HttpContext context)
    {
        if (!context.Request.Headers.ContainsKey(ForwardedHeadersDefaults.XForwardedForHeaderName))
            return;

        IPAddress? peer = context.Connection.RemoteIpAddress;
        if (peer is not null && IsTrusted(peer))
            return;

        long now = _time.GetUtcNow().UtcTicks;
        long next = Interlocked.Read(ref _nextLogTicks);
        if (now < next || Interlocked.CompareExchange(ref _nextLogTicks, now + Interval.Ticks, next) != next)
        {
            Interlocked.Increment(ref _suppressed);
            return;
        }

        long suppressed = Interlocked.Exchange(ref _suppressed, 0);
        _logger.LogWarning(
            "Ignored X-Forwarded-For from {Peer}, which is not a trusted proxy ({Section}); {Suppressed} more since the last warning",
            peer?.ToString() ?? "an unknown peer", ForwardedHeadersSetup.Section, suppressed);
    }

    private bool IsTrusted(IPAddress peer)
    {
        if (peer.IsIPv4MappedToIPv6)
            peer = peer.MapToIPv4();
        return _options.KnownProxies.Contains(peer) || _options.KnownIPNetworks.Any(n => n.Contains(peer));
    }
}
