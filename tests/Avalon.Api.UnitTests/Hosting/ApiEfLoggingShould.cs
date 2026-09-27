using Avalon.Hosting.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Avalon.Api.UnitTests.Hosting;

/// <summary>
/// #558: EF's command log ("Executed DbCommand", one entry per statement) reached the logs at
/// Information, and the api had no override at all. The api's logging, composed as Program.cs
/// composes it (the service defaults' OpenTelemetry, then AddCustomLogging), keeps every EF
/// category at Warning for every logging provider.
/// </summary>
public class ApiEfLoggingShould
{
    private const string CommandCategory = "Microsoft.EntityFrameworkCore.Database.Command";

    [Theory]
    [InlineData(CommandCategory)]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("Microsoft.EntityFrameworkCore.Query")]
    public async Task Log_ef_at_warning_and_above_only(string category)
    {
        await using WebApplication app = Build([]);
        ILogger logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(category);

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
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

    /// <summary>The logging lines of Program.cs, in its order.</summary>
    private static WebApplication Build(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        builder.Services.AddCustomLogging(builder.Configuration);
        return builder.Build();
    }
}
