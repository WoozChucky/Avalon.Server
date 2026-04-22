using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Extensions;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Controllers;

[ApiController]
[Authorize(Policy = AvalonRoles.Player)]
[Route("map-template")]
public class MapTemplateController : BaseController
{
    private readonly IMapTemplateRepository _repository;

    public MapTemplateController(IMapTemplateRepository repository)
    {
        _repository = repository;
    }

    [HttpGet(Name = "ListMapTemplates")]
    [ProducesResponseType(typeof(PagedResult<MapTemplateDto>), StatusCodes.Status200OK)]
    public async Task<PagedResult<MapTemplateDto>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var filter = new MapTemplatePaginateFilters
        {
            Page = page < 1 ? 1 : page,
            PageSize = pageSize is < 1 or > 50 ? 50 : pageSize,
        };

        var result = await _repository.PaginateAsync(filter, track: false, ct);
        return result.MapTo(ToDto);
    }

    [HttpGet("{id:int}", Name = "GetMapTemplateById")]
    [ProducesResponseType(typeof(MapTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] ushort id, CancellationToken ct)
    {
        var template = await _repository.FindByIdAsync(new MapTemplateId(id), track: false, ct);
        return template is null ? NotFound() : Ok(ToDto(template));
    }

    private static MapTemplateDto ToDto(MapTemplate t) => new()
    {
        Id = t.Id.Value,
        Name = t.Name,
        Description = t.Description,
        Directory = t.Directory,
        MapType = t.MapType,
        PvP = t.PvP,
        MinLevel = t.MinLevel,
        MaxLevel = t.MaxLevel,
        AreaTableId = t.AreaTableId,
        LoadingScreenId = t.LoadingScreenId,
        CorpseX = t.CorpseX,
        CorpseY = t.CorpseY,
        CorpseZ = t.CorpseZ,
        MaxPlayers = t.MaxPlayers,
        DefaultSpawnX = t.DefaultSpawnX,
        DefaultSpawnY = t.DefaultSpawnY,
        DefaultSpawnZ = t.DefaultSpawnZ,
        ReturnMapId = t.ReturnMapId,
    };
}
