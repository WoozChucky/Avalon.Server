using Avalon.Database.Migrator.Console.Configuration;
using Avalon.Database.Migrator.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace Avalon.Database.Migrator.Console;

internal class Program
{
    private static IServiceProvider ServiceProvider { get; set; } = null!;
    private static IConfigurationRoot Configuration { get; set; } = null!;
    private static ILogger<Program> Logger { get; set; } = null!;
    private static AppConfiguration? AppConfiguration { get; set; }
    private static IDatabaseMigrator? DatabaseMigrator { get; set; }
    
    private static async Task Main(string[] args)
    {
        System.Console.CancelKeyPress += ConsoleOnCancelKeyPress;
        
        ConfigureConfiguration(args);
        ConfigureDependencyInjection();

        try
        {
            await DatabaseMigrator!.RunAsync();
        }
        catch (Exception e)
        {
            Logger.LogError(e, "An error occurred while running the application");
            Environment.Exit(1);
        }
        
        Environment.Exit(0);
    }

    private static void ConsoleOnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        Logger.LogInformation("Application cancelled by user");
        Environment.Exit(1);
    }

    private static void ConfigureConfiguration(string[] args)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", false, false)
            .AddEnvironmentVariables();
            
        if (args.Length > 0)
        {
            builder.AddCommandLine(args);
        }
            
        Configuration = builder.Build();

        AppConfiguration = new AppConfiguration();
            
        Configuration.Bind(AppConfiguration);
    }
    
    private static void ConfigureDependencyInjection()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder
                .AddSerilog(new LoggerConfiguration()
                    .MinimumLevel.Verbose()
                    .Enrich.FromLogContext()
                    .WriteTo.Console(LogEventLevel.Debug, outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] [{SourceContext}] -> {Message}{NewLine}{Exception}", theme: AnsiConsoleTheme.Sixteen, applyThemeToRedirectedOutput: true)
                    .CreateLogger()
                )
                .SetMinimumLevel(LogLevel.Debug);
        });
        
        services.AddSingleton(AppConfiguration!);
        services.AddSingleton(AppConfiguration!.Database!);
        services.AddSingleton(AppConfiguration.Migrator!);

        services.AddDatabaseMigrator();
        
        ServiceProvider = services.BuildServiceProvider();
        
        Logger = ServiceProvider.GetService<ILogger<Program>>() ?? throw new InvalidOperationException();
        DatabaseMigrator = ServiceProvider.GetService<IDatabaseMigrator>() ?? throw new InvalidOperationException();
    }
}
