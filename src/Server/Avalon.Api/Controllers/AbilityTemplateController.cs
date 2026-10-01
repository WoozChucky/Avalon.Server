using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Api.Templates;
using Avalon.Api.Worlds;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Extensions;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Controllers;

[ApiController]
[Authorize(Policy = AvalonRoles.Player)]
[WorldScoped]
[Route("world/{worldId:int}/ability-template")]
public class AbilityTemplateController : BaseController
{
    private readonly IAbilityTemplateRepository _repository;
    private readonly ICurrentWorld _world;
    private readonly TemplateEditingOptions _editing;

    public AbilityTemplateController(
        IAbilityTemplateRepository repository,
        ICurrentWorld world,
        IOptions<TemplateEditingOptions> editing)
    {
        _repository = repository;
        _world = world;
        _editing = editing.Value;
    }

    [HttpGet(Name = "ListAbilityTemplates")]
    [ProducesResponseType(typeof(PagedResult<AbilityTemplateDto>), StatusCodes.Status200OK)]
    public async Task<PagedResult<AbilityTemplateDto>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var filter = new AbilityTemplatePaginateFilters
        {
            Page = page < 1 ? 1 : page,
            PageSize = pageSize is < 1 or > 50 ? 50 : pageSize,
        };

        var result = await _repository.PaginateAsync(filter, track: false, ct);
        return result.MapTo(ToDto);
    }

    [HttpGet("{id:long}", Name = "GetAbilityTemplateById")]
    [ProducesResponseType(typeof(AbilityTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] uint id, CancellationToken ct)
    {
        var template = await _repository.FindByIdAsync(new AbilityId(id), track: false, ct);
        if (template is null)
            return NotFound();

        AbilityTemplateDto dto = ToDto(template);
        Response.Headers.ETag = $"\"{dto.Version}\"";
        return Ok(dto);
    }

    private AbilityTemplateDto ToDto(AbilityTemplate t) => new()
    {
        Id = t.Id.Value,
        Version = TemplateVersion.Of(t),
        Editable = _world.Id is { } world && _editing.IsEditable(world),
        Name = t.Name,
        CastTime = t.CastTime,
        Cooldown = t.Cooldown,
        Cost = t.Cost,
        CostPowerType = (Avalon.Api.Contract.PowerType)t.CostPowerType,
        ScriptName = t.ScriptName,
        Range = (Avalon.Api.Contract.SpellRange)t.Range,
        Effects = (Avalon.Api.Contract.SpellEffect)t.Effects,
        EffectValue = t.EffectValue,
        AllowedClasses = t.AllowedClasses is null ? [] : t.AllowedClasses.ToList(),
    };
}
