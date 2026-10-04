using Avalon.Api;
using Avalon.Api.Authentication;
using Avalon.Api.Middlewares;
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
        var builder = WebApplication.CreateBuilder(new[]
        {
            "--Logging:OpenTelemetry:LogLevel:AspNet.Security.OpenId.Steam.SteamAuthenticationHandler=Trace",
            "--Logging:Serilog:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics=Trace",
            "--Serilog:MinimumLevel:Override:Microsoft.AspNetCore.Hosting.Diagnostics=Verbose",
        });
        builder.AddLoggingAndServiceDefaults(builder.Configuration);
        await using var app = builder.Build();
        var logs = app.Services.GetRequiredService<ILoggerFactory>();
        Assert.False(logs.CreateLogger("AspNet.Security.OpenId.Steam.SteamAuthenticationHandler").IsEnabled(LogLevel.Critical));
        Assert.False(logs.CreateLogger("Microsoft.AspNetCore.Hosting.Diagnostics").IsEnabled(LogLevel.Information));
        Assert.True(logs.CreateLogger(typeof(RequestLoggingMiddleware).FullName!).IsEnabled(LogLevel.Information));
        var options = app.Services.GetRequiredService<IOptions<AspNetCoreTraceInstrumentationOptions>>().Value;
        var callback = new DefaultHttpContext(); callback.Request.Path = SteamWebLinkOptions.CallbackPath;
        Assert.False(options.Filter!(callback));
        callback.Request.Path = "/account"; Assert.True(options.Filter(callback));
    }

    [Fact]
    public void Authenticate_and_domain_separate_protected_provider_state()
    {
        var format = new SteamOpenIdStateFormat(new GameAuthCryptography(new byte[32]));
        var properties = new AuthenticationProperties(); properties.Items["secret"] = "private-correlation";
        var state = format.Protect(properties);
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
