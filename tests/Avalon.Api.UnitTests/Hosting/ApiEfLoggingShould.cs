using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using Xunit;

namespace Avalon.Api.UnitTests.Hosting;

/// <summary>
/// #558: EF's command log ("Executed DbCommand", one entry per statement) reached the logs at
/// Information, and the api had no override at all. The api's logging, composed as Program.cs
/// composes it, keeps every EF category at Warning for every logging provider. #562: the
/// OpenTelemetry provider is one of them, registered after Serilog's cleared the others.
/// </summary>
public class ApiEfLoggingShould
{
    private const string CommandCategory = "Microsoft.EntityFrameworkCore.Database.Command";

    /// <summary>What the chart and the Aspire AppHost set, and what turns on the OTLP exporter.</summary>
    private const string OtlpEndpoint = "--OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317";

    [Theory]
    [InlineData(CommandCategory)]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("Microsoft.EntityFrameworkCore.Query")]
    public async Task Log_ef_at_warning_and_above_only(string category)
    {
        await using WebApplication app = Build([OtlpEndpoint]);
        ILogger logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(category);

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
    }

    /// <summary>#562: AddCustomLogging's ClearProviders used to remove it, so no log left over OTLP.</summary>
    [Fact]
    public async Task Register_the_open_telemetry_logging_provider()
    {
        await using WebApplication app = Build([OtlpEndpoint]);

        Assert.Contains(app.Services.GetServices<ILoggerProvider>(), provider => provider is OpenTelemetryLoggerProvider);
    }

    /// <summary>
    /// A provider-specific rule beats a general one whatever its category, so a default level for
    /// the OpenTelemetry provider alone must not bring EF's Information entries back to it.
    /// </summary>
    [Fact]
    public async Task Keep_ef_at_warning_when_a_provider_default_is_raised()
    {
        await using WebApplication app = Build([OtlpEndpoint, "--Logging:OpenTelemetry:LogLevel:Default=Information"]);
        ILogger logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(CommandCategory);

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
    }

    /// <summary>
    /// The OpenTelemetry provider's own EF rule can still be raised, for Development. Only that
    /// provider is raised here, so this also proves the api has one and the #558 rule reaches it.
    /// </summary>
    [Fact]
    public async Task Let_configuration_raise_the_command_log_for_open_telemetry()
    {
        await using WebApplication app = Build([OtlpEndpoint, $"--Logging:OpenTelemetry:LogLevel:{CommandCategory}=Information"]);
        ILogger logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(CommandCategory);

        Assert.True(logger.IsEnabled(LogLevel.Information));
    }

    /// <summary>Serilog's EF rule can still be raised through configuration, for Development.</summary>
    [Fact]
    public async Task Let_configuration_raise_the_command_log_again()
    {
        await using WebApplication app = Build(
        [
            $"--Logging:Serilog:LogLevel:{CommandCategory}=Information",
            $"--Serilog:MinimumLevel:Override:{CommandCategory}=Information",
        ]);
        ILogger logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(CommandCategory);

        Assert.True(logger.IsEnabled(LogLevel.Information));
    }

    /// <summary>The logging line of Program.cs.</summary>
    private static WebApplication Build(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.AddLoggingAndServiceDefaults(builder.Configuration);
        return builder.Build();
    }
}
