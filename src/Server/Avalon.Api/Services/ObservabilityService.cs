// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using Avalon.Api.Contract;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Presence;
using Microsoft.Extensions.Logging;
using AvalonWorld = Avalon.Domain.Auth.World;

namespace Avalon.Api.Services;

public interface IObservabilityService
{
    Task<PagedResult<OnlinePlayerDto>> GetOnlineAsync(PresencePaginateFilters filters, CancellationToken ct = default);
    Task<PlayerPresenceDto?> GetPlayerPresenceAsync(uint characterId, CancellationToken ct = default);
    Task<InstancePresenceDto?> GetInstancePresenceAsync(Guid instanceId, CancellationToken ct = default);
}

/// <summary>
/// Reads the live presence snapshots that world servers publish to Redis and shapes them
/// for admin tooling.
///
/// Note on paging: presence lives in Redis, not Postgres, so every world blob is read and
/// then paged/filtered in memory. This caps the browser payload and matches the house
/// PaginateCharacters pattern, but unlike DB paging it does not reduce backend work. If
/// the schema later splits to per-instance keys, this needs revisiting.
///
/// World enumeration goes straight to <see cref="IWorldRepository"/> rather than
/// <see cref="IWorldService"/>: the latter's ListAsync is paginated (capped at 50 per
/// page) for admin-grid use, which would silently truncate the world set this service
/// needs to scan in full. IReplicatedCache exposes no SCAN/KEYS, so the worlds table is
/// the only way to discover which world ids to look up.
/// </summary>
public class ObservabilityService : IObservabilityService
{
    private readonly IReplicatedCache _cache;
    private readonly IWorldRepository _worlds;
    private readonly IMapTemplateRepository _maps;
    private readonly ILogger<ObservabilityService> _logger;

    public ObservabilityService(
        IReplicatedCache cache,
        IWorldRepository worlds,
        IMapTemplateRepository maps,
        ILogger<ObservabilityService> logger)
    {
        _cache = cache;
        _worlds = worlds;
        _maps = maps;
        _logger = logger;
    }

    public async Task<PagedResult<OnlinePlayerDto>> GetOnlineAsync(
        PresencePaginateFilters filters, CancellationToken ct = default)
    {
        int page = filters.Page < 1 ? 1 : filters.Page;
        int pageSize = filters.PageSize is < 1 or > 50 ? 20 : filters.PageSize;

        List<OnlinePlayerDto> rows = [];
        foreach (AvalonWorld world in await _worlds.FindAllAsync(track: false, ct))
        {
            ushort worldId = world.Id.Value;
            if (filters.WorldId is { } wantWorld && worldId != wantWorld) continue;

            WorldPresenceSnapshot? snapshot = await ReadWorldAsync(worldId);
            if (snapshot is null) continue;

            foreach (InstancePresenceSnapshot instance in snapshot.Instances)
            {
                if (filters.TemplateId is { } wantTemplate && instance.TemplateId != wantTemplate) continue;

                string templateName = await TemplateNameAsync(instance.TemplateId, ct);
                foreach (CharacterPresenceSnapshot c in instance.Characters)
                {
                    rows.Add(new OnlinePlayerDto
                    {
                        CharacterId = c.CharacterId,
                        Name = c.Name,
                        Class = c.Class,
                        Level = c.Level,
                        WorldId = worldId,
                        WorldName = world.Name,
                        TemplateId = instance.TemplateId,
                        TemplateName = templateName,
                        MapType = ParseMapType(instance.MapType),
                        InstanceId = instance.InstanceId,
                        LastSeen = c.LastSeen,
                    });
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(filters.NameLike))
        {
            rows = rows
                .Where(r => r.Name.Contains(filters.NameLike, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // Stable ordering: without it, paging over a dictionary-derived list can repeat
        // or drop rows between requests.
        rows = rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                   .ThenBy(r => r.CharacterId)
                   .ToList();

        List<OnlinePlayerDto> pageItems = rows.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new PagedResult<OnlinePlayerDto>(page, pageSize, rows.Count, pageItems);
    }

    public async Task<PlayerPresenceDto?> GetPlayerPresenceAsync(uint characterId, CancellationToken ct = default)
    {
        string? rawIndex = await _cache.GetAsync(CacheKeys.CharacterPresenceIndex(characterId));
        if (rawIndex is null) return null;

        CharacterPresenceIndex? index = PresenceJson.Deserialize<CharacterPresenceIndex>(rawIndex);
        if (index is null) return null;

        WorldPresenceSnapshot? snapshot = await ReadWorldAsync(index.WorldId);
        if (snapshot is null) return null;

        InstancePresenceSnapshot? instance =
            snapshot.Instances.FirstOrDefault(i => i.InstanceId == index.InstanceId);
        CharacterPresenceSnapshot? target =
            instance?.Characters.FirstOrDefault(c => c.CharacterId == characterId);
        if (instance is null || target is null) return null;

        return new PlayerPresenceDto
        {
            Target = ToDto(target),
            Instance = await ToDtoAsync(instance, index.WorldId, ct),
            LayoutStale = false, // set by Task 7 once the stamp comparison lands
            CapturedAt = snapshot.CapturedAt,
        };
    }

    public async Task<InstancePresenceDto?> GetInstancePresenceAsync(Guid instanceId, CancellationToken ct = default)
    {
        foreach (AvalonWorld world in await _worlds.FindAllAsync(track: false, ct))
        {
            WorldPresenceSnapshot? snapshot = await ReadWorldAsync(world.Id.Value);
            InstancePresenceSnapshot? instance =
                snapshot?.Instances.FirstOrDefault(i => i.InstanceId == instanceId);
            if (instance is not null) return await ToDtoAsync(instance, world.Id.Value, ct);
        }
        return null;
    }

    private async Task<WorldPresenceSnapshot?> ReadWorldAsync(ushort worldId)
    {
        string? raw = await _cache.GetAsync(CacheKeys.WorldPresence(worldId));
        return raw is null ? null : PresenceJson.Deserialize<WorldPresenceSnapshot>(raw);
    }

    private async Task<string> TemplateNameAsync(ushort templateId, CancellationToken ct)
    {
        MapTemplate? template = await _maps.FindByIdAsync(new MapTemplateId(templateId), track: false, ct);
        return template?.Name ?? $"#{templateId}";
    }

    private async Task<InstancePresenceDto> ToDtoAsync(
        InstancePresenceSnapshot instance, ushort worldId, CancellationToken ct) => new()
    {
        InstanceId = instance.InstanceId,
        TemplateId = instance.TemplateId,
        TemplateName = await TemplateNameAsync(instance.TemplateId, ct),
        Seed = instance.Seed,
        MapType = ParseMapType(instance.MapType),
        WorldId = worldId,
        OwnerCharacterId = instance.OwnerCharacterId,
        Characters = instance.Characters.Select(ToDto).ToList(),
    };

    private static CharacterPresenceDto ToDto(CharacterPresenceSnapshot c) => new()
    {
        CharacterId = c.CharacterId,
        Name = c.Name,
        Class = c.Class,
        X = c.X, Y = c.Y, Z = c.Z,
        Orientation = c.Orientation,
        Level = c.Level,
        CurrentHealth = c.CurrentHealth,
        Health = c.Health,
        MoveState = c.MoveState,
        InCombat = c.InCombat,
        Dead = c.Dead,
        LastSeen = c.LastSeen,
    };

    /// <summary>
    /// Snapshots carry MapType as a string so a redeploy cannot renumber it underneath a
    /// live Redis value. Values this build does not recognise map to null rather than
    /// throwing (there is deliberately no "Unknown" enum member).
    /// </summary>
    private static MapType? ParseMapType(string mapType) =>
        Enum.TryParse(mapType, ignoreCase: true, out MapType parsed)
            ? parsed
            : null;
}
