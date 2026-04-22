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
[Route("spell-template")]
public class SpellTemplateController : BaseController
{
    private readonly ISpellTemplateRepository _repository;

    public SpellTemplateController(ISpellTemplateRepository repository)
    {
        _repository = repository;
    }

    [HttpGet(Name = "ListSpellTemplates")]
    [ProducesResponseType(typeof(PagedResult<SpellTemplateDto>), StatusCodes.Status200OK)]
    public async Task<PagedResult<SpellTemplateDto>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var filter = new SpellTemplatePaginateFilters
        {
            Page = page < 1 ? 1 : page,
            PageSize = pageSize is < 1 or > 50 ? 50 : pageSize,
        };

        var result = await _repository.PaginateAsync(filter, track: false, ct);
        return result.MapTo(ToDto);
    }

    [HttpGet("{id:long}", Name = "GetSpellTemplateById")]
    [ProducesResponseType(typeof(SpellTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] uint id, CancellationToken ct)
    {
        var template = await _repository.FindByIdAsync(new SpellId(id), track: false, ct);
        return template is null ? NotFound() : Ok(ToDto(template));
    }

    private static SpellTemplateDto ToDto(SpellTemplate t) => new()
    {
        Id = t.Id.Value,
        Name = t.Name,
        CastTime = t.CastTime,
        Cooldown = t.Cooldown,
        Cost = t.Cost,
        SpellScript = t.SpellScript,
        Range = (Avalon.Api.Contract.SpellRange)t.Range,
        Effects = (Avalon.Api.Contract.SpellEffect)t.Effects,
        EffectValue = t.EffectValue,
        AllowedClasses = t.AllowedClasses is null ? [] : t.AllowedClasses.Select(c => (Avalon.Api.Contract.CharacterClass)c).ToList(),
    };
}
