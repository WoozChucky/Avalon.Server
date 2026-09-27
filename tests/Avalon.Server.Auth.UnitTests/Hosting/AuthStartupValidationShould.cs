using Avalon.Hosting;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Server.Auth.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Avalon.Server.Auth.UnitTests.Hosting;

/// <summary>
/// #543: the auth host refuses to start, naming the setting, when the certificate, the auth
/// database or the cache is not configured. The host is composed as its entry point composes it,
/// over the appsettings.json it ships with, and the refusal comes from <see cref="AuthStartup"/>,
/// the startup work Program runs between Build and Run, before its migration and its cache
/// connection: every database here points at a port nothing listens on, so a check that came
/// after them would fail with a connection error instead.
/// </summary>
public class AuthStartupValidationShould
{
    private const string Unreachable = "Host=127.0.0.1;Port=1;Timeout=1;Database=none;Username=none;Password=none";

    [Fact]
    public async Task Pass_startup_validation_with_the_shipped_settings()
    {
        using IHost host = await BuildAsync(new Dictionary<string, string?>(StringComparer.Ordinal));

        host.Services.GetRequiredService<IStartupValidator>().Validate();
    }

    [Theory]
    [InlineData("Hosting:Security:CertificatePath", "'HostingSecurity' members: 'CertificatePath'")]
    [InlineData("Database:Auth:ConnectionString", "Database:Auth:ConnectionString is required.")]
    [InlineData("Cache:Host", "'CacheConfiguration' members: 'Host'")]
    public async Task Refuse_to_start_without_a_required_setting_before_any_database_call(string setting, string named)
    {
        using IHost host = await BuildAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Database:Auth:ConnectionString"] = Unreachable,
            [setting] = "",
        });

        var refused = await Assert.ThrowsAsync<OptionsValidationException>(() => AuthStartup.PrepareAsync(host));

        Assert.Contains(named, refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public async Task Refuse_to_start_with_an_online_sweep_interval_below_one_second(string seconds)
    {
        using IHost host = await BuildAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Database:Auth:ConnectionString"] = Unreachable,
            ["Application:OnlineSweepIntervalSeconds"] = seconds,
        });

        var refused = await Assert.ThrowsAsync<OptionsValidationException>(() => AuthStartup.PrepareAsync(host));

        Assert.Contains("OnlineSweepIntervalSeconds", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("TcpKeepAliveTimeSeconds", "0")]
    [InlineData("TcpKeepAliveIntervalSeconds", "-1")]
    [InlineData("TcpKeepAliveRetryCount", "0")]
    public async Task Refuse_to_start_with_a_tcp_keepalive_setting_below_one(string setting, string value)
    {
        using IHost host = await BuildAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Database:Auth:ConnectionString"] = Unreachable,
            ["Hosting:" + setting] = value,
        });

        var refused = await Assert.ThrowsAsync<OptionsValidationException>(() => AuthStartup.PrepareAsync(host));

        Assert.Contains(setting, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Give_the_auth_server_the_configured_online_sweep_interval()
    {
        using IHost host = await BuildAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Application:OnlineSweepIntervalSeconds"] = "7",
        });

        AuthServer server = Assert.Single(host.Services.GetServices<IHostedService>().OfType<AuthServer>());

        Assert.Equal(TimeSpan.FromSeconds(7), server.OnlineSweepInterval);
    }

    private static async Task<IHost> BuildAsync(Dictionary<string, string?> overrides)
    {
        string workingDirectory = Directory.GetCurrentDirectory();
        try
        {
            HostApplicationBuilder builder = await AvalonHostBuilder.CreateHostAsync([], ComponentType.Auth);
            builder.Configuration.AddInMemoryCollection(overrides);
            builder.Services.AddHostedService<AuthServer>();
            builder.Services.AddAuthServices();
            return builder.Build();
        }
        finally
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
    }
}
