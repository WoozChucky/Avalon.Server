using Avalon.Api.Contract;
using Avalon.Api.Hosting.Worlds;
using Avalon.Api.Worlds.Services;
using Avalon.Database.Auth.Repositories;
using Avalon.Infrastructure;
using Avalon.Infrastructure.WorldPlaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;
using WorldEntity = Avalon.Domain.Auth.World;

namespace Avalon.Api.Worlds.Controllers;

/// <summary>
/// The configured public world, from Application:PublicWorldId, and the head count that reads as busy for a world
/// with no player limit, from Application:PublicPopulationReference.
/// </summary>
public sealed record PublicWorldSettings(ushort? DefaultWorldId, int? PopulationReference = null);

[AllowAnonymous]
[ApiController]
[Route("public/world")]
public class PublicWorldController(IWorldRepository worlds, IWorldDatabases databases, PublicWorldSettings settings,
    IWorldReadiness readiness, IWorldOccupancy occupancy, TimeProvider clock) : ControllerBase
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

        // Only worlds this caller may read are read from Redis, so an anonymous caller learns nothing of a staff-only world.
        DateTime nowUtc = clock.GetUtcNow().UtcDateTime;
        PublicWorldDto[] dtos = await Task.WhenAll(readable.Select(w => ToDtoAsync(w, nowUtc, ct)));

        return new PublicWorldsDto { DefaultWorldId = chosen, Worlds = dtos.ToList() };
    }

    private async Task<PublicWorldDto> ToDtoAsync(WorldEntity w, DateTime nowUtc, CancellationToken ct)
    {
        WorldLiveState live = await WorldLiveState.ReadAsync(readiness, occupancy, w, nowUtc, ct);
        return new PublicWorldDto
        {
            Id = w.Id.Value,
            Name = w.Name,
            Type = (Avalon.Api.Contract.WorldType)w.Type,
            Status = (Avalon.Api.Contract.WorldStatus)live.Status,
            Population = PopulationBand.Of(live.Status, live.Occupancy.PlayerCount, w.PlayerLimit, settings.PopulationReference),
            QueueLength = live.Occupancy.QueueLength,
            MaintenanceDeadlineUtc = w.MaintenanceEnabled ? w.MaintenanceDeadlineUtc : null,
            Lore = w.Lore,
        };
    }
}
