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

    /// <summary>
    /// Saves an edit. Needs an Admin, a world listed in Application:Templates:EditableWorlds, and the version that was
    /// read as If-Match. The body is validated the way the world validates the row before it is stored.
    /// </summary>
    [HttpPut("{id:long}", Name = "UpdateAbilityTemplate")]
    [Authorize(Policy = AvalonRoles.Admin)]
    [ServiceFilter(typeof(TemplateEditGuard))]
    [ProducesResponseType(typeof(TemplateSaveResultDto<AbilityTemplateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> Update(
        [FromRoute] uint id,
        [FromBody] UpdateAbilityTemplateRequest request,
        [FromServices] TemplateEditService edit,
        CancellationToken ct)
    {
        TemplateEditResult<AbilityTemplate> result = await edit.EditAbilityAsync(
            new TemplateEditCaller(_world.Id!, Account!.Id), id, Request.Headers.IfMatch.ToString(), request, ct);
        return result.Outcome switch
        {
            TemplateEditOutcome.NotFound => NotFound(),
            TemplateEditOutcome.Conflict => TemplateProblems.Conflict(),
            TemplateEditOutcome.Invalid => ValidationProblem(new ValidationProblemDetails(result.Errors!)),
            _ => Ok(new TemplateSaveResultDto<AbilityTemplateDto>
            {
                Template = ToDto(result.Row!),
                Reload = new TemplateReloadDto { Status = result.Reload!.Status, Summary = result.Reload.Summary },
            }),
        };
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
        AuraId = t.AuraId?.Value,
    };
}
