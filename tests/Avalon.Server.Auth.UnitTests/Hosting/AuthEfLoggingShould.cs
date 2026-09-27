using Avalon.Hosting;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Server.Auth.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Avalon.Server.Auth.UnitTests.Hosting;

/// <summary>
/// #558: EF's command log ("Executed DbCommand", one entry per statement) reached the collected
/// logs at Information. The auth host, composed as its entry point composes it (OpenTelemetry
/// included), keeps every EF category at Warning for every logging provider.
/// </summary>
public class AuthEfLoggingShould
{
    private const string CommandCategory = "Microsoft.EntityFrameworkCore.Database.Command";

    [Theory]
    [InlineData(CommandCategory)]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("Microsoft.EntityFrameworkCore.Query")]
    public async Task Log_ef_at_warning_and_above_only(string category)
    {
        using IHost host = await BuildAsync([]);
        ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(category);

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
    }

    /// <summary>
    /// A provider-specific rule beats a general one whatever its category, so a default level for
    /// the OpenTelemetry provider alone must not bring EF's Information entries back to it.
    /// </summary>
    [Fact]
    public async Task Keep_ef_at_warning_when_a_provider_default_is_raised()
    {
        using IHost host = await BuildAsync(["--Logging:OpenTelemetry:LogLevel:Default=Information"]);
        ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(CommandCategory);

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
    }

    /// <summary>The OpenTelemetry provider's own EF rule can still be raised, for Development.</summary>
    [Fact]
    public async Task Let_configuration_raise_the_command_log_for_open_telemetry()
    {
        using IHost host = await BuildAsync([$"--Logging:OpenTelemetry:LogLevel:{CommandCategory}=Information"]);
        ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(CommandCategory);

        Assert.True(logger.IsEnabled(LogLevel.Information));
    }

    /// <summary>Serilog's EF rule can still be raised through configuration, for Development.</summary>
    [Fact]
    public async Task Let_configuration_raise_the_command_log_again()
    {
        using IHost host = await BuildAsync(
        [
            $"--Logging:Serilog:LogLevel:{CommandCategory}=Information",
            $"--Serilog:MinimumLevel:Override:{CommandCategory}=Information",
        ]);
        ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(CommandCategory);

        Assert.True(logger.IsEnabled(LogLevel.Information));
    }

    private static async Task<IHost> BuildAsync(string[] args)
    {
        string workingDirectory = Directory.GetCurrentDirectory();
        try
        {
            HostApplicationBuilder builder = await AvalonHostBuilder.CreateHostAsync(args, ComponentType.Auth);
            builder.ConfigureOpenTelemetry();
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
