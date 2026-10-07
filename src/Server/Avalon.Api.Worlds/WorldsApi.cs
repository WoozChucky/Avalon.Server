using System.Reflection;
using Avalon.Api.Hosting;
using Avalon.Api.Hosting.Middlewares;
using Avalon.Api.Hosting.Worlds;
using Avalon.Api.Worlds.Exceptions;

namespace Avalon.Api.Worlds;

/// <summary>
/// Worlds (#794, design section 2.1): the world registry and its maintenance, world content and its live editing,
/// characters, the views across worlds (<c>/character</c>, presence), public tooltips and link previews, and the
/// balance workbench's proxy. It needs Redis (readiness, presence, the maintenance, reload and script channels), both
/// databases of every world and the world routes (<c>/world/{worldId}/...</c>). It reads accounts and writes the
/// worlds table in the auth database, whose schema identity migrates.
/// </summary>
public sealed class WorldsApi : IApiService
{
    public static readonly WorldsApi Service = new();

    private WorldsApi()
    {
    }

    public string Name => "worlds";

    public Assembly ControllerAssembly => typeof(WorldsApi).Assembly;

    public ApiServiceNeeds Needs { get; } = new(Redis: true, WorldDatabases: WorldDatabaseParts.Both,
        AuthSchema: AuthSchemaRole.Reader, WorldRoutes: true);

    public void AddServices(WebApplicationBuilder builder)
    {
        builder.Services.AddWorlds(builder.Configuration);
        builder.Services.AddSingleton<IExceptionProblemMapper, WorldsProblemMapper>();
    }
}
