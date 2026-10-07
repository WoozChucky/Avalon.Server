using Avalon.Api.Contract;
using Avalon.Api.Hosting.Authentication;
using Avalon.Api.Hosting.Worlds;
using Avalon.Api.Worlds.Templates;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Extensions;
using Avalon.Database.World.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WorldData = Avalon.Domain.World;
using Avalon.Api.Hosting.Controllers;

namespace Avalon.Api.Worlds.Controllers;

/// <summary>Aura templates (auras): read by any player, edited live by an admin on an editable world.</summary>
[ApiController]
[Authorize(Policy = AvalonRoles.Player)]
[WorldScoped]
[Route("world/{worldId:int}/aura-template")]
public class AuraTemplateController(
    IAuraTemplateRepository repository,
    ICurrentWorld world,
    IOptions<TemplateEditingOptions> editing) : BaseController
{
    private readonly TemplateEditingOptions _editing = editing.Value;

    [HttpGet(Name = "ListAuraTemplates")]
    [ProducesResponseType(typeof(PagedResult<AuraTemplateDto>), StatusCodes.Status200OK)]
    public async Task<PagedResult<AuraTemplateDto>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        PagedResult<WorldData.AuraTemplate> result = await repository.PaginateAsync(page < 1 ? 1 : page,
            pageSize is < 1 or > 50 ? 50 : pageSize, ct);
        return result.MapTo(ToDto);
    }

    [HttpGet("{id:long}", Name = "GetAuraTemplateById")]
    [ProducesResponseType(typeof(AuraTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] uint id, CancellationToken ct)
    {
        WorldData.AuraTemplate? template = await repository.FindByIdAsync(new AuraId(id), ct);
        if (template is null)
            return NotFound();

        AuraTemplateDto dto = ToDto(template);
        Response.Headers.ETag = $"\"{dto.Version}\"";
        return Ok(dto);
    }

    /// <summary>
    /// Saves an edit, its modifier list replacing the stored one. Needs an Admin, a world listed in
    /// Application:Templates:EditableWorlds, and the version that was read as If-Match. Validated the way the world
    /// validates the row before it is stored; the world then reloads its auras.
    /// </summary>
    [HttpPut("{id:long}", Name = "UpdateAuraTemplate")]
    [Authorize(Policy = AvalonRoles.Admin)]
    [ServiceFilter(typeof(TemplateEditGuard))]
    [ProducesResponseType(typeof(TemplateSaveResultDto<AuraTemplateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> Update([FromRoute] uint id, [FromBody] UpdateAuraTemplateRequest request,
        [FromServices] TemplateEditService edit, CancellationToken ct)
    {
        TemplateEditResult<WorldData.AuraTemplate> result = await edit.EditAuraAsync(
            new TemplateEditCaller(world.Id!, Account!.Id), id, Request.Headers.IfMatch.ToString(), request, ct);
        return result.Outcome switch
        {
            TemplateEditOutcome.NotFound => NotFound(),
            TemplateEditOutcome.Conflict => TemplateProblems.Conflict(),
            TemplateEditOutcome.Invalid => ValidationProblem(new ValidationProblemDetails(result.Errors!)),
            _ => Ok(new TemplateSaveResultDto<AuraTemplateDto>
            {
                Template = ToDto(result.Row!),
                Reload = new TemplateReloadDto { Status = result.Reload!.Status, Summary = result.Reload.Summary },
            }),
        };
    }

    private AuraTemplateDto ToDto(WorldData.AuraTemplate t) => new()
    {
        Id = t.Id.Value,
        Version = TemplateVersion.Of(t),
        Editable = world.Id is { } id && _editing.IsEditable(id),
        Name = t.Name,
        Icon = t.Icon,
        Kind = (AuraKind)t.Kind,
        DurationMs = t.DurationMs,
        TickIntervalMs = t.TickIntervalMs,
        PeriodicKind = (AuraPeriodicKind)t.PeriodicKind,
        PeriodicBase = t.PeriodicBase,
        ScalingStat = (AbilityScalingStat)t.ScalingStat,
        ScalingCoefficient = t.ScalingCoefficient,
        BaseDamageCoefficient = t.BaseDamageCoefficient,
        Stacking = (AuraStacking)t.Stacking,
        MaxStacks = t.MaxStacks,
        ScriptName = t.ScriptName,
        Modifiers = t.Modifiers.OrderBy(m => m.Stat)
            .Select(m => new AuraStatModifierDto { Stat = (AuraStat)m.Stat, Value = m.Value, Kind = (AuraModifierKind)m.Kind })
            .ToList(),
    };
}
