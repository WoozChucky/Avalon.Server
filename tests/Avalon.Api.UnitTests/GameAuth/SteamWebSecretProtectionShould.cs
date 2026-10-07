using Avalon.Api.Authentication;
using Avalon.Api.Hosting.Middlewares;
using Avalon.Infrastructure.GameAuth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Instrumentation.AspNetCore;
using Xunit;

namespace Avalon.Api.UnitTests.GameAuth;

public sealed class SteamWebSecretProtectionShould
{
    [Fact]
    public async Task Suppress_assertion_logs_and_traces_despite_verbose_provider_configuration()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new[]
        {
            "--Logging:OpenTelemetry:LogLevel:AspNet.Security.OpenId.Steam.SteamAuthenticationHandler=Trace",
            "--Logging:Serilog:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics=Trace",
            "--Serilog:MinimumLevel:Override:Microsoft.AspNetCore.Hosting.Diagnostics=Verbose",
        });
        builder.AddLoggingAndServiceDefaults(builder.Configuration);
        await using WebApplication app = builder.Build();
        ILoggerFactory logs = app.Services.GetRequiredService<ILoggerFactory>();
        Assert.False(logs.CreateLogger("AspNet.Security.OpenId.Steam.SteamAuthenticationHandler").IsEnabled(LogLevel.Critical));
        Assert.False(logs.CreateLogger("Microsoft.AspNetCore.Hosting.Diagnostics").IsEnabled(LogLevel.Information));
        Assert.True(logs.CreateLogger(typeof(RequestLoggingMiddleware).FullName!).IsEnabled(LogLevel.Information));
        AspNetCoreTraceInstrumentationOptions options = app.Services.GetRequiredService<IOptions<AspNetCoreTraceInstrumentationOptions>>().Value;
        var callback = new DefaultHttpContext(); callback.Request.Path = SteamWebLinkOptions.CallbackPath;
        Assert.False(options.Filter!(callback));
        callback.Request.Path = "/account"; Assert.True(options.Filter(callback));
    }

    /// <summary>
    /// The callback's query carries Steam's signed assertion: the request log keeps the path and leaves the query out
    /// (#794: the shared request log asks the services which queries to hide).
    /// </summary>
    [Fact]
    public void Keep_the_callbacks_query_out_of_the_request_log()
    {
        var services = new ServiceCollection();
        services.AddSteamWebLinkSecretProtection();
        using ServiceProvider provider = services.BuildServiceProvider();

        RequestLoggingOptions options = provider.GetRequiredService<IOptions<RequestLoggingOptions>>().Value;

        Assert.True(options.HidesQueryOf(SteamWebLinkOptions.CallbackPath));
        Assert.False(options.HidesQueryOf("/account"));
    }

    [Fact]
    public void Authenticate_and_domain_separate_protected_provider_state()
    {
        var format = new SteamOpenIdStateFormat(new GameAuthCryptography(new byte[32]));
        var properties = new AuthenticationProperties(); properties.Items["secret"] = "private-correlation";
        string state = format.Protect(properties);
        Assert.DoesNotContain("private-correlation", state);
        Assert.Equal("private-correlation", format.Unprotect(state)!.Items["secret"]);
        Assert.Null(format.Unprotect(state, "other-purpose"));
        Assert.Null(format.Unprotect(state[..^4] + "AAAA"));
    }

    [Theory]
    [InlineData("http://api.example.test/account/links/steam/callback", "https://web.example.test")]
    [InlineData("https://user@api.example.test/account/links/steam/callback", "https://web.example.test")]
    [InlineData("https://api.example.test/wrong", "https://web.example.test")]
    [InlineData("https://api.example.test/account/links/steam/callback?extra=1", "https://web.example.test")]
    [InlineData("https://api.example.test/account/links/steam/callback", "http://web.example.test")]
    public void Require_explicit_trusted_https_return_urls(string callback, string site) => Assert.Throws<InvalidOperationException>(() =>
        new SteamWebLinkOptions { CallbackUrl = callback, SiteUrl = site }.Validate());
}
