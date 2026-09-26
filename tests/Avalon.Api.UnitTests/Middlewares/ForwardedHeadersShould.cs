using System.Collections.Concurrent;
using System.Net;
using Avalon.Api.Config;
using Avalon.Api.Middlewares;
using Avalon.Infrastructure.Login;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Middlewares;

/// <summary>
/// #478 review: the api trusted <c>X-Forwarded-For</c> from loopback only, hard-coded, so behind
/// any other ingress every REST caller was the ingress's address, one login source for everyone:
/// ten failures by anyone locked REST login for all. The trusted proxies are now configuration,
/// loopback still the only default, a network that trusts everyone is refused, and a header from a
/// peer that is not trusted is logged, at most once per interval.
/// </summary>
public sealed class ForwardedHeadersShould
{
    private const string PeerHeader = "X-Test-Peer";
    private const string NoAddress = "no address";

    private static ForwardedHeadersConfig Bind(Dictionary<string, string?> settings)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return ApiConfiguration.Bind(configuration).ForwardedHeaders ?? new ForwardedHeadersConfig();
    }

    [Fact]
    public void Bind_the_trusted_proxies_networks_and_forward_limit_from_configuration()
    {
        ForwardedHeadersConfig config = Bind(new(StringComparer.Ordinal)
        {
            ["Application:ForwardedHeaders:KnownProxies:0"] = "10.0.0.2",
            ["Application:ForwardedHeaders:KnownNetworks:0"] = "10.1.0.0/16",
            ["Application:ForwardedHeaders:ForwardLimit"] = "2",
        });

        ForwardedHeadersOptions options = ForwardedHeadersSetup.BuildOptions(config);

        Assert.Contains(IPAddress.Parse("10.0.0.2"), options.KnownProxies);
        Assert.Contains(System.Net.IPNetwork.Parse("10.1.0.0/16"), options.KnownIPNetworks);
        Assert.Equal(2, options.ForwardLimit);
        Assert.True(ForwardedHeadersSetup.TrustsAnyProxy(config));
        // Loopback stays trusted.
        Assert.Contains(IPAddress.IPv6Loopback, options.KnownProxies);
    }

    [Fact]
    public void Trust_only_loopback_when_nothing_is_configured()
    {
        ForwardedHeadersOptions options = ForwardedHeadersSetup.BuildOptions(null);

        Assert.All(options.KnownProxies, p => Assert.True(IPAddress.IsLoopback(p)));
        Assert.All(options.KnownIPNetworks, n => Assert.True(IPAddress.IsLoopback(n.BaseAddress)));
        Assert.Equal(1, options.ForwardLimit);
        Assert.False(ForwardedHeadersSetup.TrustsAnyProxy(null));
    }

    [Theory]
    [InlineData("KnownProxies:0", "not-an-address")]
    [InlineData("KnownNetworks:0", "10.0.0.0")]
    [InlineData("KnownNetworks:0", "0.0.0.0/0")]
    [InlineData("KnownNetworks:0", "::/0")]
    [InlineData("KnownNetworks:0", "10.0.0.0/7")]
    [InlineData("KnownNetworks:0", "0.0.0.0/1")]
    [InlineData("KnownNetworks:0", "2001:db8::/31")]
    [InlineData("ForwardLimit", "0")]
    public void Refuse_to_start_with_a_setting_that_is_invalid_or_trusts_everyone(string key, string value)
    {
        ForwardedHeadersConfig config = Bind(new(StringComparer.Ordinal)
        {
            [$"Application:ForwardedHeaders:{key}"] = value,
        });

        var ex = Assert.Throws<InvalidOperationException>(() => ForwardedHeadersSetup.BuildOptions(config));

        Assert.Contains("Application:ForwardedHeaders:" + key.Split(':')[0], ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Production", false, true)]
    [InlineData("Staging", false, true)]
    [InlineData("Development", false, false)]
    [InlineData("Production", true, false)]
    public void Warn_at_startup_outside_development_when_no_proxy_is_trusted(string environment, bool configured,
        bool warns)
    {
        var logs = new CapturingLogs();
        IHostEnvironment host = Substitute.For<IHostEnvironment>();
        host.EnvironmentName.Returns(environment);
        ForwardedHeadersConfig config = configured ? new() { KnownProxies = ["10.0.0.2"] } : new();

        ForwardedHeadersSetup.WarnIfNoProxyTrusted(logs.CreateLogger("test"), config, host);

        Assert.Equal(warns, logs.Warnings.Any(m => m.Contains(ForwardedHeadersSetup.Section, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Take_the_source_from_a_trusted_proxys_forwarded_for()
    {
        await using var host = await Host.StartAsync(new ForwardedHeadersConfig { KnownProxies = ["10.0.0.2"] });

        string source = await host.SourceAsync(peer: "10.0.0.2", forwardedFor: "198.51.100.7");

        Assert.Equal(LoginSource.FromAddress(IPAddress.Parse("198.51.100.7")).Key, source);
        Assert.Empty(host.Logs.Warnings);
    }

    [Fact]
    public async Task Take_the_source_from_a_proxy_on_a_trusted_network()
    {
        await using var host = await Host.StartAsync(new ForwardedHeadersConfig { KnownNetworks = ["10.1.0.0/16"] });

        string source = await host.SourceAsync(peer: "10.1.4.5", forwardedFor: "198.51.100.7");

        Assert.Equal(LoginSource.FromAddress(IPAddress.Parse("198.51.100.7")).Key, source);
    }

    /// <summary>A caller cannot pick its own source by sending the header itself.</summary>
    [Fact]
    public async Task Ignore_forwarded_for_from_a_peer_that_is_not_trusted_and_warn_once_per_interval()
    {
        await using var host = await Host.StartAsync(new ForwardedHeadersConfig { KnownProxies = ["10.0.0.2"] });

        string first = await host.SourceAsync(peer: "203.0.113.9", forwardedFor: "198.51.100.7");
        await host.SourceAsync(peer: "203.0.113.9", forwardedFor: "198.51.100.8");
        await host.SourceAsync(peer: "203.0.113.10", forwardedFor: "198.51.100.9");

        Assert.Equal(LoginSource.FromAddress(IPAddress.Parse("203.0.113.9")).Key, first);
        Assert.Single(host.Logs.Warnings);

        host.Clock.Advance(UntrustedForwardedHeaderLog.Interval);
        await host.SourceAsync(peer: "203.0.113.9", forwardedFor: "198.51.100.7");
        Assert.Equal(2, host.Logs.Warnings.Count);
    }

    [Fact]
    public async Task Take_the_source_from_a_proxy_on_a_trusted_ipv6_network()
    {
        await using var host = await Host.StartAsync(new ForwardedHeadersConfig { KnownNetworks = ["fd00:1::/48"] });

        string source = await host.SourceAsync(peer: "fd00:1::5", forwardedFor: "2001:db8:aa::7");

        Assert.Equal(LoginSource.FromAddress(IPAddress.Parse("2001:db8:aa::7")).Key, source);
        Assert.Empty(host.Logs.Warnings);
    }

    /// <summary>
    /// Two trusted hops: with a limit of 2 the client behind both is the source; with the default
    /// of 1 only the last hop is read, so the source is the inner proxy.
    /// </summary>
    [Theory]
    [InlineData(2, "198.51.100.7")]
    [InlineData(1, "10.0.0.3")]
    public async Task Read_as_many_trusted_hops_as_the_forward_limit_allows(int limit, string expected)
    {
        await using var host = await Host.StartAsync(new ForwardedHeadersConfig
        {
            KnownProxies = ["10.0.0.2", "10.0.0.3"],
            ForwardLimit = limit,
        });

        string source = await host.SourceAsync(peer: "10.0.0.2", forwardedFor: "198.51.100.7, 10.0.0.3");

        Assert.Equal(LoginSource.FromAddress(IPAddress.Parse(expected)).Key, source);
    }

    /// <summary>
    /// #478 re-review: a request with no peer address that carried X-Forwarded-For got the address
    /// it named, so it chose its own source and escaped the 400 an address-less caller gets. The
    /// header is dropped for it, and logged.
    /// </summary>
    [Fact]
    public async Task Drop_forwarded_for_from_a_caller_with_no_peer_address()
    {
        await using var host = await Host.StartAsync(new ForwardedHeadersConfig { KnownProxies = ["10.0.0.2"] });

        string source = await host.SourceAsync(peer: null, forwardedFor: "198.51.100.7");

        Assert.Equal(NoAddress, source);
        Assert.Contains(host.Logs.Warnings, m => m.Contains("no peer address", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Not_warn_for_a_request_without_forwarded_for()
    {
        await using var host = await Host.StartAsync(new ForwardedHeadersConfig());

        await host.SourceAsync(peer: "203.0.113.9", forwardedFor: null);

        Assert.Empty(host.Logs.Warnings);
    }

    private sealed class Host : IAsyncDisposable
    {
        private WebApplication _app = null!;
        private HttpClient _client = null!;

        public CapturingLogs Logs { get; } = new();
        public ManualClock Clock { get; } = new();

        public static async Task<Host> StartAsync(ForwardedHeadersConfig config)
        {
            var host = new Host();
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(host.Logs);
            builder.Services.AddSingleton<TimeProvider>(host.Clock);
            builder.Services.AddSingleton(ForwardedHeadersSetup.BuildOptions(config));
            builder.Services.AddSingleton<UntrustedForwardedHeaderLog>();

            host._app = builder.Build();
            // The test server has no socket, so each request names its peer.
            host._app.Use((context, next) =>
            {
                string peer = context.Request.Headers[PeerHeader].ToString();
                context.Connection.RemoteIpAddress = peer.Length == 0 ? null : IPAddress.Parse(peer);
                return next(context);
            });
            host._app.UseAvalonForwardedHeaders();
            host._app.MapGet("/source", (HttpContext context) =>
                context.Connection.RemoteIpAddress is { } address ? LoginSource.FromAddress(address).Key : NoAddress);
            await host._app.StartAsync();
            host._client = host._app.GetTestClient();
            return host;
        }

        public async Task<string> SourceAsync(string? peer, string? forwardedFor)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/source");
            if (peer != null) request.Headers.Add(PeerHeader, peer);
            if (forwardedFor != null) request.Headers.Add("X-Forwarded-For", forwardedFor);
            using HttpResponseMessage response = await _client.SendAsync(request);
            return await response.Content.ReadAsStringAsync();
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.DisposeAsync();
        }
    }

    internal sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    internal sealed class CapturingLogs : ILoggerProvider, ILogger
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

        public IReadOnlyList<string> Warnings =>
            _entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ToList();

        public ILogger CreateLogger(string categoryName) => this;
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => _entries.Enqueue((logLevel, formatter(state, exception)));
    }
}
