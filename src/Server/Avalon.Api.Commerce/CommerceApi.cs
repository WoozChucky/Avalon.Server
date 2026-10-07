using System.Reflection;
using Avalon.Api.Hosting;
using Avalon.Api.Hosting.Middlewares;
using Avalon.Api.Hosting.Worlds;
using Avalon.Infrastructure.Extensions;

namespace Avalon.Api.Commerce;

/// <summary>
/// Commerce (#794, design section 2.1): checkout, purchase and licence status, the admin purchase tools, the payment
/// provider's notifications and the reconciliation worker. It needs Redis (the checkout budget) and no world database;
/// it reads and writes the auth database's purchase tables, whose schema identity migrates.
/// </summary>
public sealed class CommerceApi : IApiService
{
    public static readonly CommerceApi Service = new();

    private CommerceApi()
    {
    }

    public string Name => "commerce";

    public Assembly ControllerAssembly => typeof(CommerceApi).Assembly;

    public ApiServiceNeeds Needs { get; } = new(Redis: true, WorldDatabases: WorldDatabaseParts.None,
        AuthSchema: AuthSchemaRole.Reader, WorldRoutes: false);

    public void AddServices(WebApplicationBuilder builder)
    {
        IServiceCollection services = builder.Services;

        // Purchases and licences are scoped to the store's environment and identity prefix. Identity validates the
        // section's Steam settings; commerce reads only those two, so it binds the section without that validation.
        services.AddStoreAuthenticationOptions();
        // The options, the provider, the purchase services and the reconciliation worker.
        services.AddCommerce();

        services.AddSingleton<IExceptionProblemMapper, CommerceProblemMapper>();
    }
}
