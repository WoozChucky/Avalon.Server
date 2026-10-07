using System.Reflection;
using Avalon.Api.Hosting;
using Avalon.Api.Hosting.Middlewares;
using Avalon.Api.Hosting.Worlds;
using Avalon.Api.Identity.Authentication;
using Avalon.Api.Identity.Config;
using Avalon.Api.Identity.Exceptions;
using Avalon.Api.Identity.Services.Email;
using Avalon.Infrastructure.Login;

namespace Avalon.Api.Identity;

/// <summary>
/// Identity (#794, design section 2.1): accounts and their credentials, MFA, personal access tokens, web and launcher
/// sessions and the tokens they are issued, store and Steam sign-in, account links and consolidation, game contexts,
/// join tickets, game admission and the game workload listener, email verification and change, and push device
/// registration. It needs Redis (the login budgets, MFA, launcher codes, game tickets and contexts) and the Characters
/// database of every world (character ownership, gameplay fences, account consolidation), and it owns the auth
/// database's schema. Before authentication it keeps the game workload routes to the workload listener and answers the
/// Steam callback; after authentication it authenticates the game workloads.
/// </summary>
public sealed class IdentityApi : IApiService
{
    public static readonly IdentityApi Service = new();

    private IdentityApi()
    {
    }

    public string Name => "identity";

    public Assembly ControllerAssembly => typeof(IdentityApi).Assembly;

    public ApiServiceNeeds Needs { get; } = new(Redis: true, WorldDatabases: WorldDatabaseParts.Characters,
        AuthSchema: AuthSchemaRole.Owner, WorldRoutes: false, SignsTokens: true);

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

        services.AddSingleton<IExceptionProblemMapper, IdentityProblemMapper>();

        services.AddIdentityAuthentication();
        services.AddIdentity(applicationConfig);
        // Application:Email (#510): no sender unless configured, and then email change is off (501).
        services.AddEmail(applicationConfig.Email, builder.Environment);
    }

    public void UseBeforeAuthentication(IApplicationBuilder app)
    {
        // First: on any port but the game workload listener's, the game workload routes do not exist (design D7.4).
        app.UseGameInternalRoutes(GameWorkloadHosting.PortOf(app.ApplicationServices.GetRequiredService<IConfiguration>()));
        app.UseMiddleware<SteamOpenIdCallbackMiddleware>();
    }

    public void UseAfterAuthentication(IApplicationBuilder app) =>
        app.UseGameWorkloadAuthentication();

    public void LogStartup(IServiceProvider services, ILogger logger)
    {
        LoginLimitsValidation.LogAtStartup(logger, services.GetRequiredService<ILoginLimits>(), "Application:Authentication");
        logger.LogInformation("Application:Email:Sender is {EmailSender}; email change is {EmailChange}",
            (services.GetRequiredService<ApplicationConfig>().Email ?? new EmailConfig()).Sender,
            services.GetService<IEmailSender>() is null ? "off (501)" : "on");
    }
}
