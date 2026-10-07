using System.Reflection;
using Avalon.Api.Hosting;
using Avalon.Api.Hosting.Middlewares;
using Avalon.Api.Hosting.Worlds;

namespace Avalon.Api.Distribution;

/// <summary>
/// Client distribution (#794, design section 2.1): the launcher installer and its update feed, releases, the
/// changelog, channels and their manifests, read from the build store (<c>Application:Distribution</c>). It needs no
/// Redis and no world database, and only reads the auth schema, to check a caller's credentials.
/// </summary>
public sealed class DistributionApi : IApiService
{
    public static readonly DistributionApi Service = new();

    /// <summary>The section that names the build store.</summary>
    public const string Section = "Application:Distribution";

    private DistributionApi()
    {
    }

    public string Name => "distribution";

    public Assembly ControllerAssembly => typeof(DistributionApi).Assembly;

    public ApiServiceNeeds Needs { get; } = new(Redis: false, WorldDatabases: WorldDatabaseParts.None,
        AuthSchema: AuthSchemaRole.Reader, WorldRoutes: false);

    public void AddServices(WebApplicationBuilder builder)
    {
        IServiceCollection services = builder.Services;

        // The build store (homelab Garage). Without it the /client endpoints answer 503.
        DistributionConfiguration distribution = builder.Configuration.GetSection(Section).Get<DistributionConfiguration>() ?? new();
        if (distribution.IsConfigured)
            services.AddSingleton<IDistributionStore>(new S3DistributionStore(distribution));
        else
            services.AddSingleton<IDistributionStore, UnconfiguredDistributionStore>();
        services.AddMemoryCache();
        services.AddSingleton<ClientDistributionService>();

        services.AddSingleton<IExceptionProblemMapper, DistributionProblemMapper>();
    }
}
