using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Api.Templates;
using Avalon.Api.Worlds;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.Scripts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Controllers;

/// <summary>
/// The script names one world accepts in its templates, for the admin app's script dropdown. Published by the world
/// itself, so a world that has not reported in answers with <c>published: false</c> and empty lists.
/// </summary>
[Authorize(Policy = AvalonRoles.GameMaster)]
[ApiController]
[WorldScoped]
[Route("world/{worldId:int}/scripts")]
public class WorldScriptsController : BaseController
{
    private readonly IWorldScriptCatalog _catalog;
    private readonly ICurrentWorld _world;

    public WorldScriptsController(IWorldScriptCatalog catalog, ICurrentWorld world)
    {
        _catalog = catalog;
        _world = world;
    }

    [HttpGet(Name = "GetWorldScripts")]
    [ProducesResponseType(typeof(WorldScriptCatalogDto), StatusCodes.Status200OK)]
    public async Task<WorldScriptCatalogDto> Get(CancellationToken ct)
    {
        WorldId world = _world.Id ?? throw new InvalidOperationException("No world selected for this request.");
        ScriptCatalogSnapshot? catalog = await _catalog.GetAsync(world, ct);
        return catalog is null
            ? new WorldScriptCatalogDto()
            : new WorldScriptCatalogDto
            {
                Ai = [.. catalog.Ai], Ability = [.. catalog.Ability], Quest = [.. catalog.Quest],
                Item = [.. catalog.Item ?? []], Published = true,
            };
    }
}
