using Avalon.Api.Contract;
using Avalon.Api.Contract.Mappers;
using Avalon.Api.Hosting.Worlds;
using Avalon.Api.Worlds.Previews;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;
using WorldEntity = Avalon.Domain.Auth.World;
using WorldId = Avalon.Domain.Auth.WorldId;

namespace Avalon.Api.Worlds.Controllers;

/// <summary>
/// Server-rendered link previews for the public item and ability pages. Discord, X and Slack do not run
/// JavaScript, so the public site's nginx sends their bots here. The world comes from <c>?world=N</c>, or
/// is the default <c>GET /public/world</c> names; the access rules and the 404-before-503 order are those
/// of <see cref="PublicWorldScopedAttribute"/> endpoints, though the route names no world.
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("public/preview")]
[Produces("text/html")]
public class PublicPreviewController(
    IWorldRepository worlds,
    IWorldDatabases databases,
    CurrentWorld currentWorld,
    PublicWorldSettings worldSettings,
    PublicSiteSettings site,
    IOptions<PreviewConfiguration> previews,
    IItemTemplateRepository items,
    IAbilityTemplateRepository abilities) : ControllerBase
{
    [HttpGet("item/{id:long}", Name = "GetItemPreview")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/html")]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound, "text/html")]
    public async Task<IActionResult> GetItem([FromRoute] ulong id, [FromQuery] string? world, CancellationToken ct)
    {
        (WorldEntity? chosen, bool open, IActionResult? refusal) = await SelectWorldAsync(world, ct);
        if (chosen is null) return refusal!;

        ItemTemplate? template = await items.FindByIdAsync(new ItemTemplateId(id), track: false, ct);
        if (template is null) return NotFoundPage();

        PublicItemDto item = template.ToPublicDto();
        return Page(open, LinkPreviewPage.Render(item.Name, LinkPreviewText.Describe(item),
            PagePath("item", id, world, chosen), site.Base, previews.Value.ColourOf(item.Rarity), previews.Value.Site));
    }

    [HttpGet("ability/{id:long}", Name = "GetAbilityPreview")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/html")]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound, "text/html")]
    public async Task<IActionResult> GetAbility([FromRoute] uint id, [FromQuery] string? world, CancellationToken ct)
    {
        (WorldEntity? chosen, bool open, IActionResult? refusal) = await SelectWorldAsync(world, ct);
        if (chosen is null) return refusal!;

        AbilityTemplate? template = await abilities.FindByIdAsync(new AbilityId(id), track: false, ct);
        if (template is null) return NotFoundPage();

        PublicAbilityDto ability = template.ToPublicDto();
        return Page(open, LinkPreviewPage.Render(ability.Name, LinkPreviewText.Describe(ability),
            PagePath("ability", id, world, chosen), site.Base, previews.Value.AbilityTheme, previews.Value.Site));
    }

    /// <summary>
    /// The request's world, as the middleware picks it for <c>/public/world/{worldId}/...</c> (parse, auth row,
    /// access rule, configured, then available), or the default world when none is named. A refusal is
    /// the same 404 page whichever step failed, and 503 only for a world the caller may read.
    /// </summary>
    private async Task<(WorldEntity? World, bool OpenToEveryone, IActionResult? Refusal)> SelectWorldAsync(
        string? named, CancellationToken ct)
    {
        AccountAccessLevel caller = await PublicCaller.AccessLevelAsync(HttpContext);
        WorldEntity? world;
        if (named is null)
        {
            List<WorldEntity> readable = await PublicWorlds.ReadableAsync(worlds, databases, caller, ct);
            ushort? chosen = PublicWorlds.DefaultOf(readable, worldSettings.DefaultWorldId);
            world = readable.FirstOrDefault(w => w.Id.Value == chosen);
        }
        else
        {
            if (!WorldDatabaseSettings.TryParseWorldId(named, out WorldId? id)) return (null, false, NotFoundPage());
            world = await worlds.FindByIdAsync(id, track: false, ct);
            if (world is null
                || !AccessLevels.ForWorld(world.AccessLevelRequired).Allows(caller)
                || !databases.TryGet(id, out _))
            {
                return (null, false, NotFoundPage());
            }

            if (!databases.IsAvailable(id))
                return (null, false, Html(StatusCodes.Status503ServiceUnavailable, LinkPreviewPage.Unavailable(previews.Value.Site)));
        }

        if (world is null) return (null, false, NotFoundPage());

        currentWorld.Select(world.Id, world.Name);
        // The default depends on the caller, so only an anonymous-equivalent caller's is shared.
        bool open = AccessLevels.ForWorld(world.AccessLevelRequired).Allows(AccountAccessLevel.Player)
                    && (named is not null || caller == AccountAccessLevel.Player);
        return (world, open, null);
    }

    /// <summary>The page's path on the public site; <c>?world=N</c> only when the request named a world.</summary>
    private static string PagePath(string kind, ulong id, string? named, WorldEntity world) =>
        $"/{kind}/{id}" + (named is null ? "" : $"?world={world.Id.Value}");

    private ContentResult Page(bool open, string html)
    {
        Response.Headers.CacheControl = open ? "public, max-age=300" : "private, max-age=300";
        return Html(StatusCodes.Status200OK, html);
    }

    private ContentResult NotFoundPage() => Html(StatusCodes.Status404NotFound, LinkPreviewPage.NotFound(previews.Value.Site));

    private static ContentResult Html(int status, string html) =>
        new() { StatusCode = status, ContentType = LinkPreviewPage.ContentType, Content = html };
}
