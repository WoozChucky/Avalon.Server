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
[Route("world/{worldId:int}/creature-template")]
public class CreatureTemplateController : BaseController
{
    private readonly ICreatureTemplateRepository _repository;
    private readonly ICurrentWorld _world;
    private readonly TemplateEditingOptions _editing;

    public CreatureTemplateController(
        ICreatureTemplateRepository repository,
        ICurrentWorld world,
        IOptions<TemplateEditingOptions> editing)
    {
        _repository = repository;
        _world = world;
        _editing = editing.Value;
    }

    [HttpGet(Name = "ListCreatureTemplates")]
    [ProducesResponseType(typeof(PagedResult<CreatureTemplateDto>), StatusCodes.Status200OK)]
    public async Task<PagedResult<CreatureTemplateDto>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var filter = new CreatureTemplatePaginateFilters
        {
            Page = page < 1 ? 1 : page,
            PageSize = pageSize is < 1 or > 50 ? 50 : pageSize,
        };

        PagedResult<CreatureTemplate> result = await _repository.PaginateAsync(filter, track: false, ct);
        return result.MapTo(ToDto);
    }

    [HttpGet("{id:long}", Name = "GetCreatureTemplateById")]
    [ProducesResponseType(typeof(CreatureTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] ulong id, CancellationToken ct)
    {
        CreatureTemplate? template = await _repository.FindByIdAsync(new CreatureTemplateId(id), track: false, ct);
        if (template is null)
            return NotFound();

        CreatureTemplateDto dto = ToDto(template);
        Response.Headers.ETag = $"\"{dto.Version}\"";
        return Ok(dto);
    }

    /// <summary>
    /// Saves an edit. Needs an Admin, a world listed in Application:Templates:EditableWorlds, and the version that was
    /// read as If-Match. The body is validated the way the world validates the row before it is stored.
    /// </summary>
    [HttpPut("{id:long}", Name = "UpdateCreatureTemplate")]
    [Authorize(Policy = AvalonRoles.Admin)]
    [ServiceFilter(typeof(TemplateEditGuard))]
    [ProducesResponseType(typeof(TemplateSaveResultDto<CreatureTemplateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> Update(
        [FromRoute] ulong id,
        [FromBody] UpdateCreatureTemplateRequest request,
        [FromServices] TemplateEditService edit,
        CancellationToken ct)
    {
        TemplateEditResult<CreatureTemplate> result = await edit.EditCreatureAsync(
            new TemplateEditCaller(_world.Id!, Account!.Id), id, Request.Headers.IfMatch.ToString(), request, ct);
        return result.Outcome switch
        {
            TemplateEditOutcome.NotFound => NotFound(),
            TemplateEditOutcome.Conflict => TemplateProblems.Conflict(),
            TemplateEditOutcome.Invalid => ValidationProblem(new ValidationProblemDetails(result.Errors!)),
            _ => Ok(new TemplateSaveResultDto<CreatureTemplateDto>
            {
                Template = ToDto(result.Row!),
                Reload = new TemplateReloadDto { Status = result.Reload!.Status, Summary = result.Reload.Summary },
            }),
        };
    }

    private CreatureTemplateDto ToDto(CreatureTemplate t) => new()
    {
        Id = t.Id.Value,
        Version = TemplateVersion.Of(t),
        Editable = _world.Id is { } world && _editing.IsEditable(world),
        Name = t.Name,
        SubName = t.SubName,
        IconName = t.IconName,
        MinLevel = t.MinLevel,
        MaxLevel = t.MaxLevel,
        SpeedWalk = t.SpeedWalk,
        SpeedRun = t.SpeedRun,
        SpeedSwim = t.SpeedSwim,
        Rarity = t.Rarity,
        Family = (Avalon.Api.Contract.CreatureFamily)t.Family,
        Type = (Avalon.Api.Contract.CreatureType)t.Type,
        LootTableId = t.LootTableId?.Value,
        MinGold = t.MinGold,
        MaxGold = t.MaxGold,
        MovementType = t.MovementType,
        DetectionRange = t.DetectionRange,
        MovementId = t.MovementId,
        ScriptName = t.ScriptName,
        HealthModifier = t.HealthModifier,
        ManaModifier = t.ManaModifier,
        ArmorModifier = t.ArmorModifier,
        ExperienceModifier = t.ExperienceModifier,
        RegenHealth = t.RegenHealth,
        DmgSchool = t.DmgSchool,
        DamageModifier = t.DamageModifier,
        BaseAttackTime = t.BaseAttackTime,
        RangeAttackTime = t.RangeAttackTime,
        Experience = t.Experience,
        BodyRemoveTimerSecs = t.BodyRemoveTimerSecs,
    };
}
