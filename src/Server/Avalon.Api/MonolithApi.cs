using System.Reflection;
using Avalon.Api.Authentication;
using Avalon.Api.Commerce;
using Avalon.Api.Config;
using Avalon.Api.Exceptions;
using Avalon.Api.Hosting;
using Avalon.Api.Hosting.Middlewares;
using Avalon.Api.Hosting.Worlds;
using Avalon.Api.Services;
using Avalon.Api.Services.Email;
using Avalon.Infrastructure.Login;

namespace Avalon.Api;

/// <summary>
/// The API services still in Avalon.Api, as it ran before the split (#794): identity, worlds and commerce, which is
/// what <see cref="ServiceRegistration"/> registers, the game workload listener, the Steam callback before
/// authentication and the workload authentication after it. Temporary: each service moves out into its own library,
/// with its own descriptor (<see cref="ApiServices"/> lists them), and this goes.
/// </summary>
public sealed class MonolithApi : IApiService
{
    public static readonly MonolithApi Service = new();

    /// <summary>Redis, both databases of every world, the auth schema's owner, and the world routes.</summary>
    public static readonly ApiServiceNeeds MonolithNeeds = new(Redis: true, WorldDatabases: WorldDatabaseParts.Both,
        AuthSchema: AuthSchemaRole.Owner, WorldRoutes: true);

    /// <summary>How the services' own exceptions are answered, in the order the middleware asks.</summary>
    public static IReadOnlyList<IExceptionProblemMapper> ProblemMappers { get; } =
    [
        new CommerceProblemMapper(),
        new IdentityProblemMapper(),
        new WorldsProblemMapper(),
    ];

    private MonolithApi()
    {
    }

    public string Name => "monolith";

    public Assembly ControllerAssembly => typeof(MonolithApi).Assembly;

    public ApiServiceNeeds Needs => MonolithNeeds;

    public void ConfigureBuilder(WebApplicationBuilder builder) =>
        builder.ConfigureGameWorkloadListener();

    public void AddServices(WebApplicationBuilder builder)
    {
        IServiceCollection services = builder.Services;
        services.AddSteamWebLinkSecretProtection();

        var applicationConfig = ApplicationConfig.Bind(builder.Configuration);
        services.AddSingleton(applicationConfig);
        services.AddSingleton(applicationConfig.Environment!);
        services.AddSingleton(applicationConfig.Authentication!);
        services.AddSingleton(applicationConfig.Notification!);
        services.AddSingleton(applicationConfig.Cache!);

        foreach (IExceptionProblemMapper mapper in ProblemMappers)
            services.AddSingleton(mapper);

        services.AddServiceAuthentication();
        services.AddInfrastructure(applicationConfig);
        // Application:Email (#510): no sender unless configured, and then email change is off (501).
        services.AddEmail(applicationConfig.Email, builder.Environment);
    }

    public void UseBeforeAuthentication(IApplicationBuilder app) =>
        app.UseMiddleware<SteamOpenIdCallbackMiddleware>();

    public void UseAfterAuthentication(IApplicationBuilder app) =>
        app.UseGameWorkloadAuthentication();

    public void LogStartup(IServiceProvider services, ILogger logger)
    {
        LoginLimitsValidation.LogAtStartup(logger, services.GetRequiredService<ILoginLimits>(), "Application:Authentication");
        logger.LogInformation("Application:Email:Sender is {EmailSender}; email change is {EmailChange}",
            (services.GetRequiredService<ApplicationConfig>().Email ?? new EmailConfig()).Sender,
            services.GetService<IEmailSender>() is null ? "off (501)" : "on");
    }

    /// <summary>Starts the workers, none of which exist (#794 deletes the interface with the monolith).</summary>
    public async Task StartAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Program");
        foreach (IWorkerService workerService in services.GetServices<IWorkerService>())
        {
            logger.LogInformation("Starting worker {Worker}", workerService.GetType().Name);
            await workerService.StartWorker(cancellationToken);
        }
    }
}
