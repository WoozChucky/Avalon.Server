using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Api.Worlds;
using Avalon.Common.Accounts;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;
using Avalon.Database.Auth.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
        AccountAccessLevel caller = await PublicCaller.AccessLevelAsync(HttpContext);

        List<PublicWorldDto> readable = [];
        foreach (ConfiguredWorld configured in databases.All)
        {
            if (!databases.IsAvailable(configured.Id)) continue;
            var world = await worlds.FindByIdAsync(configured.Id, track: false, ct);
            if (world is null || !AccessLevels.ForWorld(world.AccessLevelRequired).Allows(caller)) continue;
            readable.Add(new PublicWorldDto { Id = world.Id.Value, Name = world.Name });
        }

        ushort? chosen = readable.Any(w => w.Id == settings.DefaultWorldId)
            ? settings.DefaultWorldId
            : readable.FirstOrDefault()?.Id;

        return new PublicWorldsDto { DefaultWorldId = chosen, Worlds = readable };
    }
}
