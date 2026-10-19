using Avalon.Api.Hosting;
using Avalon.Api.Hosting.Authentication;
using Avalon.Api.Hosting.Authentication.Jwt;
using Avalon.Api.Hosting.Middlewares;
using Avalon.Api.Identity.Authentication;
using Avalon.Api.Identity.Authentication.Jwt;
using Avalon.Api.Identity.Config;
using Avalon.Api.Identity.LoadTest;
using Avalon.Api.Identity.Services;
using Avalon.Api.Identity.Services.Email;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Extensions;
using Avalon.Infrastructure.Login;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avalon.Api.Identity;

/// <summary>
/// What identity registers (#794), as Avalon.Api registered it before the split: everything but the shared hosting,
/// which <see cref="AvalonApiHost"/> registers. <see cref="IdentityApi"/> calls it.
/// </summary>
public static class IdentityServiceRegistration
{
    /// <summary>
    /// The host's logging and service defaults (<see cref="AvalonApiHost.AddApiLoggingAndServiceDefaults"/>), and the
    /// Steam web link's protection of its secrets in the logs and traces they reach.
    /// </summary>
    public static void AddLoggingAndServiceDefaults(this WebApplicationBuilder builder, IConfiguration configuration)
    {
        builder.AddApiLoggingAndServiceDefaults(configuration);
        builder.Services.AddSteamWebLinkSecretProtection();
    }

    public static void AddSteamStoreAuthentication(this IServiceCollection services)
    {
        // Bound once in a process, which commerce may share (#794).
        services.AddStoreAuthenticationOptions();
        services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<Avalon.Configuration.StoreAuthenticationConfiguration>,
            Config.StoreAuthenticationOptionsValidator>();
        ConfigureSteamHttp(services.AddHttpClient<Avalon.Infrastructure.StoreAuth.ISteamProofVerifier,
            Avalon.Infrastructure.StoreAuth.SteamProofVerifier>());
        ConfigureSteamHttp(services.AddHttpClient<Avalon.Infrastructure.StoreAuth.ISteamOwnershipClient,
            Avalon.Infrastructure.StoreAuth.SteamOwnershipClient>());
        services.AddScoped<Avalon.Infrastructure.StoreAuth.IGameIdentityProvider, Avalon.Infrastructure.StoreAuth.SteamIdentityProvider>();
        services.AddScoped<Avalon.Infrastructure.StoreAuth.IGameLicenseProvider, Avalon.Infrastructure.StoreAuth.SteamLicenseProvider>();
    }

#pragma warning disable EXTEXP0001 // Fixed-host provider transport owns its one-retry budget.
    private static void ConfigureSteamHttp(IHttpClientBuilder http) => http
        .RemoveAllLoggers().RemoveAllResilienceHandlers()
        .ConfigureAdditionalHttpMessageHandlers((handlers, _) =>
        {
            // Drop default service discovery/resilience for the fixed Valve origin. URL-bearing loggers
            // are removed above; suppress provider spans because Valve requires credentials in GET queries.
            handlers.Clear();
            handlers.Add(new Authentication.SteamSecretProtectionHandler());
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            MaxConnectionsPerServer = 32,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            ActivityHeadersPropagator = null,
        });
#pragma warning restore EXTEXP0001

    /// <summary>
    /// Identity's services. The shared hosting they run on, the auth database, the world databases and their
    /// repositories, Redis, the forwarded headers, the request rate limiter and the time provider, is registered by
    /// <see cref="ApiHostingRegistration.AddApiHosting"/>.
    /// </summary>
    public static void AddIdentity(this IServiceCollection services, ApplicationConfig config)
    {
        services.AddSteamStoreAuthentication();
        services.AddSteamWebLink();
        // Its own key since #801 (design D4.3), with the bytes the HS256 signing key had; checked before the api serves
        // (IdentityStartupCheck), not here, so OpenAPI generation needs none.
        services.AddSingleton(_ => GameAuthHostKey.From(config.GameAuth));
        services.AddSingleton(sp => new Avalon.Infrastructure.GameAuth.GameAuthCryptography(
            sp.GetRequiredService<GameAuthHostKey>().Bytes));
        services.AddSingleton<Avalon.Infrastructure.GameAuth.IGameContextStore, Avalon.Infrastructure.GameAuth.RedisGameContextStore>();
        services.AddSingleton<Avalon.Infrastructure.GameAuth.AuthAttemptStore>();
        services.AddScoped<Avalon.Infrastructure.GameAuth.IGameContextRevocations, Avalon.Infrastructure.GameAuth.GameContextRevocations>();
        services.AddScoped<Avalon.Infrastructure.GameAuth.GameAuthorizationService>();
        services.AddScoped<Avalon.Infrastructure.StoreAuth.GameProviderRegistry>();
        services.AddScoped<Avalon.Infrastructure.GameAuth.GameLicenseAuthorityService>();
        services.AddScoped<Avalon.Infrastructure.StoreAuth.IGameLicenseProvider, Avalon.Infrastructure.StoreAuth.AvalonLicenseProvider>();
        services.AddScoped<Avalon.Infrastructure.GameAuth.PendingLinkStore>();
        services.AddScoped<Avalon.Infrastructure.GameAuth.JoinTicketStore>();
        services.AddSingleton<Avalon.Infrastructure.GameAuth.GameApplicationAccessPolicy>();
        services.AddScoped<GameSessionFenceService>();
        services.AddScoped<AccountConsolidationService>();
        services.AddScoped<Avalon.Infrastructure.GameAuth.IGameServerAllocator, GameServerAllocator>();
        // The place ledger and the queue (#967): identity is their only writer.
        services.AddOptions<Avalon.Configuration.WorldPlacesConfiguration>()
            .BindConfiguration(Avalon.Configuration.WorldPlacesConfiguration.Section)
            .ValidateDataAnnotations()
            .Validate(c => c.IsValid(), $"{Avalon.Configuration.WorldPlacesConfiguration.Section}: each check-in timeout must be above its interval.")
            .Validate(c => c.CoversHeldPlace(), $"{Avalon.Configuration.WorldPlacesConfiguration.Section}: UnclaimedPlaceMinutes (in minutes) must cover HeldPlaceSeconds plus 60 s, so a dropped player's held place is never swept past the unclaimed horizon.")
            .Validate(c => c.CoversTurnWindow(), $"{Avalon.Configuration.WorldPlacesConfiguration.Section}: PausedTurnMinutes (in minutes) must cover TurnWindowSeconds.")
            .ValidateOnStart();
        services.AddSingleton<Avalon.Infrastructure.WorldPlaces.IWorldPlaceStore, Avalon.Infrastructure.WorldPlaces.RedisWorldPlaceStore>();
        services.AddSingleton<Avalon.Infrastructure.WorldPlaces.PlaceLedgerOutage>();
        services.AddSingleton(_ => new Avalon.Infrastructure.WorldPlaces.WorldPlaceMetrics(Avalon.Common.Telemetry.DiagnosticsConfig.Api.Meter));
        // A full world's line: the waiters' check-ins and leaves, with their own per-account limit.
        services.AddSingleton<GameWorldAvailability>();
        services.AddScoped<GameQueueService>();
        services.AddSingleton<QueueRateLimiter>();
        // Every credential identity issues is known to the queue's limiter at once, so a waiter's first check-in after a
        // refresh is not taken for a stranger's.
        services.AddSingleton<Avalon.Infrastructure.GameAuth.IGameCredentialIssued>(sp => sp.GetRequiredService<QueueRateLimiter>());
        // The gauges of places taken and held, line length and limit, sampled every 15 s (and the sweep with them).
        services.AddHostedService<WorldPlaceGauges>();
        // Places taken and line length for /game/worlds, which the worlds service reads too: registered once in a process.
        services.TryAddSingleton<Avalon.Infrastructure.WorldPlaces.IWorldOccupancy, Avalon.Infrastructure.WorldPlaces.RedisWorldOccupancy>();
        services.AddOptions<Avalon.Configuration.GameWorkloadConfiguration>().BindConfiguration("Application:GameWorkloads")
            .Validate(c => { c.Validate(); return true; })
            // The sign-in floor (#949): appsettings.json holds the default, so a value missing or unreadable stops startup.
            .Validate(c => Avalon.Infrastructure.GameAuth.ClientVersionFloor.IsValid(c.MinClientVersion),
                Avalon.Infrastructure.GameAuth.ClientVersionFloor.Refusal)
            .ValidateOnStart();
        services.AddOptions<LoadTestOptions>().BindConfiguration(LoadTestOptions.Section)
            .Validate(o => o.MaxAccounts >= 1,
                $"{LoadTestOptions.Section}:{nameof(LoadTestOptions.MaxAccounts)} must be at least 1.")
            .ValidateOnStart();
        services.AddScoped<ILoadTestAccounts, LoadTestAccountService>();
        services.AddScoped<AccountLinkReauthentication>();
        services.AddScoped<StoreAccountRegistration>();
        services.AddScoped<Avalon.Infrastructure.GameAuth.IGameAccountRegistration>(sp => sp.GetRequiredService<StoreAccountRegistration>());
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IPersonalAccessTokenService, PersonalAccessTokenService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddSingleton<ILauncherAuthCodes, LauncherAuthCodes>();
        services.AddSingleton<Avalon.Infrastructure.GameTickets.IGameTicketStore,
            Avalon.Infrastructure.GameTickets.RedisGameTicketStore>();
        services.AddMfaService();
        // The identity login policy, with the limits under
        // Application:Authentication, checked here since that section is bound without validation.
        LoginLimitsValidation.Validate(config.Authentication ?? new AuthenticationConfig(), "Application:Authentication");
        ValidateAccountCreationCap(config.Authentication ?? new AuthenticationConfig());
        ValidateEmailChangeSendCaps(config.Authentication ?? new AuthenticationConfig());
        services.AddSingleton<ILoginLimits>(sp => sp.GetRequiredService<AuthenticationConfig>());
        services.AddLoginPolicy();
        services.AddScoped<IReauthentication, Reauthentication>();
        // Launcher sign-in's own request limit (#591), on top of the shared rate limiter.
        services.AddClientAuthRateLimiting();
        services.AddSecureRandom();
        // A world's ready heartbeat, which the worlds service reads too: registered once in a process (#794).
        services.TryAddSingleton<IWorldReadiness, WorldReadiness>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IJwtUtils, JwtUtils>();
        // The store's publisher key and the Steam web link's URLs, checked before the api serves (ApiStartup).
        services.AddSingleton<IApiStartupCheck, IdentityStartupCheck>();
    }

    /// <summary>
    /// The shared token validation (<see cref="ApiAuthentication.AddApiAuthentication"/>) with what identity adds
    /// to it (<see cref="AddIdentityAuthentication"/>), as a host running identity composes the two.
    /// </summary>
    public static void AddAuth(this IServiceCollection services, ApplicationConfig config)
    {
        services.AddApiAuthentication(config.Authentication, JwtKeys.Create(config.Authentication, signsTokens: true));
        services.AddIdentityAuthentication();
    }

    /// <summary>
    /// What identity adds to the shared token validation: the game workload scheme, its policy and its rate-limit
    /// partition, and the resource authorization handlers for accounts and personal access tokens. The worlds service
    /// registers its characters' (#794).
    /// </summary>
    public static void AddIdentityAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, GameServerAuthHandler>(GameServerAuthHandler.Scheme, _ => { });
        services.AddAuthorization(options => options.AddPolicy(GameServerAuthHandler.Scheme, policy => policy
            .AddAuthenticationSchemes(GameServerAuthHandler.Scheme).RequireAuthenticatedUser()
            .RequireClaim(GameServerAuthHandler.ServerIdClaim)));
        services.AddSingleton<IRateLimitWorkloads, GameServerRateLimitWorkloads>();
        // The workload listener's refused certificates, logged with their reason (#1051).
        services.TryAddSingleton<RefusedWorkloadCertificateLog>();
        // A successful lease heartbeat's request line is Debug (#1051): one per player every 15 seconds. So is a queue
        // check-in's (#967): a full world's waiters check in every 5 or 15 seconds each.
        services.Configure<RequestLoggingOptions>(options =>
        {
            options.QuietOnSuccess.Add(GameInternalRoutes.IsHeartbeat);
            options.QuietOnSuccess.Add(path => path.Equals("/game/queue/status", StringComparison.OrdinalIgnoreCase));
        });

        services.AddScoped<IAuthorizationHandler, Authorization.AccountReadHandler>();
        services.AddScoped<IAuthorizationHandler, Authorization.AccountWriteHandler>();
        services.AddScoped<IAuthorizationHandler, Authorization.PatReadHandler>();
        services.AddScoped<IAuthorizationHandler, Authorization.PatWriteHandler>();
    }

    /// <summary>
    /// Registers the email sender <c>Application:Email:Sender</c> names (#510), or none for
    /// <see cref="EmailSenderKind.None"/>, the default. Whether an <see cref="IEmailSender"/> is
    /// registered is the one test of whether the api can send email: email change answers 501
    /// without one. Stops startup, naming the setting, for the pickup sender outside Development
    /// (its files hold confirm tokens, on the api's own disk) and for a sender with no valid
    /// <c>From</c>.
    /// </summary>
    public static void AddEmail(this IServiceCollection services, EmailConfig? config, IHostEnvironment environment)
    {
        config ??= new EmailConfig();
        ValidateEmailVerification(config, environment);
        services.AddSingleton(config);
        services.AddScoped<IAccountEmailVerificationService, AccountEmailVerificationService>();
        switch (config.Sender)
        {
            case EmailSenderKind.None:
                return;
            case EmailSenderKind.Resend:
                if (!IsBareAddress(config.From))
                    throw new InvalidOperationException($"{EmailConfig.Section}:From must be a bare email address.");
                if (config.FromName is not null && (config.FromName.Any(char.IsControl) || config.FromName.IndexOfAny(['<', '>', '"']) >= 0))
                    throw new InvalidOperationException($"{EmailConfig.Section}:FromName contains invalid header characters.");
                if (string.IsNullOrWhiteSpace(config.ResendApiKey) || config.ResendApiKey.Any(char.IsWhiteSpace))
                    throw new InvalidOperationException($"{EmailConfig.Section}:ResendApiKey must contain a server API key.");
#pragma warning disable EXTEXP0001 // A send with an unknown outcome must not be retried.
                services.AddHttpClient<ResendEmailSender>(http =>
                    {
                        http.Timeout = TimeSpan.FromSeconds(30);
                        http.MaxResponseContentBufferSize = 16 * 1024;
                    })
                    .RemoveAllLoggers().RemoveAllResilienceHandlers()
                    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
#pragma warning restore EXTEXP0001
                services.AddTransient<IEmailSender>(sp => sp.GetRequiredService<ResendEmailSender>());
                return;
            case EmailSenderKind.Pickup:
                if (!environment.IsDevelopment())
                {
                    throw new InvalidOperationException(
                        $"{EmailConfig.Section}:Sender is Pickup, which is for Development only: it writes every " +
                        $"email, confirm tokens included, to a folder on this machine. The environment is " +
                        $"'{environment.EnvironmentName}'. Set {EmailConfig.Section}:Sender to None.");
                }

                if (!IsBareAddress(config.From))
                {
                    throw new InvalidOperationException(
                        $"{EmailConfig.Section}:From must be an email address, such as noreply@example.com, " +
                        $"when {EmailConfig.Section}:Sender is Pickup.");
                }

                if (string.IsNullOrWhiteSpace(config.PickupDirectory))
                {
                    throw new InvalidOperationException(
                        $"{EmailConfig.Section}:PickupDirectory must name a folder when {EmailConfig.Section}:Sender is Pickup.");
                }

                services.AddSingleton<IEmailSender>(sp =>
                    new PickupEmailSender(config, sp.GetService<TimeProvider>() ?? TimeProvider.System));
                return;
            default:
                throw new InvalidOperationException(
                    $"{EmailConfig.Section}:Sender '{config.Sender}' is not a known sender; use None, Pickup or Resend.");
        }
    }

    private static void ValidateEmailVerification(EmailConfig config, IHostEnvironment environment)
    {
        foreach ((string? name, int value) in new[] {
            (nameof(config.VerificationCooldownSeconds), config.VerificationCooldownSeconds),
            (nameof(config.MaxVerificationSendsPerAccount), config.MaxVerificationSendsPerAccount),
            (nameof(config.MaxVerificationSendsPerSource), config.MaxVerificationSendsPerSource) })
        {
            if (value < 1) throw new InvalidOperationException($"{EmailConfig.Section}:{name} must be at least 1.");
        }
        // Omitting the origin disables current-address verification without disabling existing email change.
        if (config.VerificationSiteOrigin is null) return;
        string origin = config.VerificationSiteOrigin;
        if (origin != origin.Trim() || !Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttps && !(environment.IsDevelopment() && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/")
        {
            throw new InvalidOperationException($"{EmailConfig.Section}:VerificationSiteOrigin must be an HTTPS origin.");
        }
    }

    /// <summary>A bare address, as it goes into a From header: printable ASCII, one '@', no padding, no display name.</summary>
    private static bool IsBareAddress(string? address) =>
        address is not null
        && string.Equals(address, address.Trim(), StringComparison.Ordinal)
        && Avalon.Domain.Auth.AccountEmail.IsValid(address)
        && System.Net.Mail.MailAddress.TryCreate(address, out System.Net.Mail.MailAddress? parsed)
        && string.Equals(parsed.Address, address, StringComparison.Ordinal);

    /// <summary>
    /// Stops startup, naming the setting, when an email-change send budget (#510 review) is below
    /// one: zero would refuse every email change.
    /// </summary>
    public static void ValidateEmailChangeSendCaps(AuthenticationConfig config)
    {
        if (config.MaxEmailChangeSendsPerAccount < 1)
        {
            throw new InvalidOperationException(
                "Application:Authentication:MaxEmailChangeSendsPerAccount must be at least 1.");
        }

        if (config.MaxEmailChangeSendsPerAddress < 1)
        {
            throw new InvalidOperationException(
                "Application:Authentication:MaxEmailChangeSendsPerAddress must be at least 1.");
        }

        if (config.EmailChangeSendWindowMinutes < 1)
        {
            throw new InvalidOperationException(
                "Application:Authentication:EmailChangeSendWindowMinutes must be at least 1.");
        }
    }

    /// <summary>
    /// Stops startup, naming the setting, when the account-creation cap (#495 review) is below one:
    /// zero would refuse every registration.
    /// </summary>
    public static void ValidateAccountCreationCap(AuthenticationConfig config)
    {
        if (config.MaxAccountsCreatedPerSource < 1)
        {
            throw new InvalidOperationException(
                "Application:Authentication:MaxAccountsCreatedPerSource must be at least 1.");
        }

        if (config.AccountCreationWindowMinutes < 1)
        {
            throw new InvalidOperationException(
                "Application:Authentication:AccountCreationWindowMinutes must be at least 1.");
        }
    }
}
