using Avalon.Api.Contract;
using Avalon.Api.Contract.Mappers;
using Avalon.Api.Hosting.Worlds;
using Avalon.Api.Worlds.Services;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Worlds.Controllers;

/// <summary>
/// Item and ability tooltips for anyone: the Dashboard shows them when a reader hovers a linked item or ability. Also the
/// shared talent tree whole (#1181), for a tree viewer.
/// </summary>
[AllowAnonymous]
[ApiController]
[PublicWorldScoped]
[Route("public/world/{worldId:int}")]
public class PublicController(
    IItemTemplateRepository items, IItemModifierRepository modifiers, IAbilityTemplateRepository abilities,
    ITalentTreeRepository talents)
    : ControllerBase
{
    [HttpGet("item/{id:long}", Name = "GetPublicItem")]
    [ProducesResponseType(typeof(PublicItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetItem([FromRoute] ulong id, CancellationToken ct)
    {
        ItemTemplate? template = await items.FindByIdAsync(new ItemTemplateId(id), track: false, ct);
        if (template is null) return NotFound();
        PublicItemDto item = await PublicItems.ToPublicAsync(template, items, modifiers, ct);
        SetCacheControl();
        return Ok(item);
    }

    [HttpGet("ability/{id:long}", Name = "GetPublicAbility")]
    [ProducesResponseType(typeof(PublicAbilityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAbility([FromRoute] uint id, CancellationToken ct)
    {
        AbilityTemplate? template = await abilities.FindByIdAsync(new AbilityId(id), track: false, ct);
        if (template is null) return NotFound();
        SetCacheControl();
        return Ok(template.ToPublicDto(await talents.IsGrantedAsync(template.Id, ct)));
    }

    /// <summary>The world's talent tree as its database holds it, whether or not the world's catalog accepted it.</summary>
    [HttpGet("talents", Name = "GetPublicTalentTree")]
    [ProducesResponseType(typeof(TalentTreeDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<TalentTreeDto>> GetTalentTree(CancellationToken ct)
    {
        TalentTreeRows rows = await talents.GetTreeAsync(ct);
        SetCacheControl();
        return Ok(rows.ToDto());
    }

    private void SetCacheControl()
    {
        bool open = HttpContext.Items[PublicWorldScopedAttribute.OpenToEveryoneItem] is true;
        Response.Headers.CacheControl = open ? "public, max-age=300" : "private, max-age=300";
    }
}
