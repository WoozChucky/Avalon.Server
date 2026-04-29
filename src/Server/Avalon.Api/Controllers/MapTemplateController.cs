using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Api.Services;
using Avalon.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Controllers;

[ApiController]
[Authorize(Policy = AvalonRoles.Player)]
[Route("map-template")]
public class MapTemplateController : BaseController
{
    private readonly IMapService _service;

    public MapTemplateController(IMapService service)
    {
        _service = service;
    }

    [HttpGet(Name = "ListMapTemplates")]
    [ProducesResponseType(typeof(PagedResult<MapTemplateDto>), StatusCodes.Status200OK)]
    public Task<PagedResult<MapTemplateDto>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default) =>
        _service.ListAsync(page, pageSize, ct);

    [HttpGet("{id:int}", Name = "GetMapTemplateById")]
    [ProducesResponseType(typeof(MapTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] ushort id, CancellationToken ct)
    {
        var template = await _service.GetAsync(id, ct);
        return template is null ? NotFound() : Ok(template);
    }

    /// <summary>
    /// Admin debug: runs the procedural layout generator against the persisted
    /// <see cref="Avalon.Domain.World.ProceduralMapConfig"/> + chunk pool for a given map.
    /// If no seed is supplied a random one is picked. Returns the resulting layout as JSON
    /// for the admin SPA to render. Does not persist anything; safe to call repeatedly.
    /// </summary>
    [HttpGet("{id:int}/preview-layout", Name = "PreviewMapLayout")]
    [Authorize(Policy = AvalonRoles.Admin)]
    [ProducesResponseType(typeof(LayoutPreviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PreviewLayout(
        [FromRoute] ushort id,
        [FromQuery] int? seed,
        CancellationToken ct)
    {
        var layout = await _service.PreviewLayoutAsync(id, seed, ct);
        return layout is null ? NotFound() : Ok(layout);
    }

    /// <summary>
    /// Admin debug: returns raw .obj bytes for a chunk template's geometry file. The
    /// route segment is whitelisted against persisted <c>ChunkTemplate.GeometryFile</c>
    /// values to block path traversal. The SPA fetches one of these per unique chunk in
    /// a layout preview to render the 3D scene with three.js + OBJLoader.
    /// </summary>
    [HttpGet("chunk-asset/{*filename}", Name = "GetChunkAsset")]
    [Authorize(Policy = AvalonRoles.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChunkAsset([FromRoute] string filename, CancellationToken ct)
    {
        var asset = await _service.GetChunkAssetAsync(filename, ct);
        return asset is null ? NotFound() : File(asset.Bytes, asset.ContentType);
    }
}
