using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Api.Worlds;
using Avalon.Common.Accounts;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;
using Avalon.Database.Auth.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorldEntity = Avalon.Domain.Auth.World;

namespace Avalon.Api.Controllers;

/// <summary>The configured public world, from Application:PublicWorldId.</summary>
public sealed record PublicWorldSettings(ushort? DefaultWorldId);

[AllowAnonymous]
[ApiController]
[Route("public/world")]
public class PublicWorldController(IWorldRepository worlds, IWorldDatabases databases, PublicWorldSettings settings)
    : ControllerBase
{
    [HttpGet(Name = "ListPublicWorlds")]
    [ProducesResponseType(typeof(PublicWorldsDto), StatusCodes.Status200OK)]
    public async Task<PublicWorldsDto> List(CancellationToken ct)
    {
        // The list varies by caller (staff also see Development/PTR worlds), so it is never shared.
        Response.Headers.CacheControl = "private, no-cache";
        AccountAccessLevel caller = await PublicCaller.AccessLevelAsync(HttpContext);

        List<WorldEntity> readable = await PublicWorlds.ReadableAsync(worlds, databases, caller, ct);
        ushort? chosen = PublicWorlds.DefaultOf(readable, settings.DefaultWorldId);

        return new PublicWorldsDto { DefaultWorldId = chosen, Worlds = readable.Select(w => new PublicWorldDto { Id = w.Id.Value, Name = w.Name }).ToList() };
    }
}
