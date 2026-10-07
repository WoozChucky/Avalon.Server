using Avalon.Api.Contract;
using Avalon.Api.Contract.Mappers;
using Avalon.Api.Hosting.Worlds;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Worlds.Controllers;

/// <summary>Item and ability tooltips for anyone (the Dashboard's Wowhead-style tooltips).</summary>
[AllowAnonymous]
[ApiController]
[PublicWorldScoped]
[Route("public/world/{worldId:int}")]
public class PublicController(IItemTemplateRepository items, IAbilityTemplateRepository abilities) : ControllerBase
{
    [HttpGet("item/{id:long}", Name = "GetPublicItem")]
    [ProducesResponseType(typeof(PublicItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetItem([FromRoute] ulong id, CancellationToken ct)
    {
        ItemTemplate? template = await items.FindByIdAsync(new ItemTemplateId(id), track: false, ct);
        if (template is null) return NotFound();
        SetCacheControl();
        return Ok(template.ToPublicDto());
    }

    [HttpGet("ability/{id:long}", Name = "GetPublicAbility")]
    [ProducesResponseType(typeof(PublicAbilityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAbility([FromRoute] uint id, CancellationToken ct)
    {
        AbilityTemplate? template = await abilities.FindByIdAsync(new AbilityId(id), track: false, ct);
        if (template is null) return NotFound();
        SetCacheControl();
        return Ok(template.ToPublicDto());
    }

    private void SetCacheControl()
    {
        bool open = HttpContext.Items[PublicWorldScopedAttribute.OpenToEveryoneItem] is true;
        Response.Headers.CacheControl = open ? "public, max-age=300" : "private, max-age=300";
    }
}
