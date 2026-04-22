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
[Route("creature-template")]
public class CreatureTemplateController : BaseController
{
    private readonly ICreatureTemplateRepository _repository;

    public CreatureTemplateController(ICreatureTemplateRepository repository)
    {
        _repository = repository;
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

        var result = await _repository.PaginateAsync(filter, track: false, ct);
        return result.MapTo(ToDto);
    }

    [HttpGet("{id:long}", Name = "GetCreatureTemplateById")]
    [ProducesResponseType(typeof(CreatureTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] ulong id, CancellationToken ct)
    {
        var template = await _repository.FindByIdAsync(new CreatureTemplateId(id), track: false, ct);
        return template is null ? NotFound() : Ok(ToDto(template));
    }

    private static CreatureTemplateDto ToDto(CreatureTemplate t) => new()
    {
        Id = t.Id.Value,
        Name = t.Name,
        SubName = t.SubName,
        IconName = t.IconName,
        MinLevel = t.MinLevel,
        MaxLevel = t.MaxLevel,
        SpeedWalk = t.SpeedWalk,
        SpeedRun = t.SpeedRun,
        SpeedSwim = t.SpeedSwim,
        Rank = t.Rank,
        Family = t.Family,
        Type = t.Type,
        LootId = t.LootId,
        MinGold = t.MinGold,
        MaxGold = t.MaxGold,
        AIName = t.AIName,
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
        RespawnTimerSecs = t.RespawnTimerSecs,
        BodyRemoveTimerSecs = t.BodyRemoveTimerSecs,
    };
}
