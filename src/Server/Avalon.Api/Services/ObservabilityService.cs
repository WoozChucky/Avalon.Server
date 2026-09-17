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
using Avalon.World.ChunkLayouts;
using Microsoft.Extensions.Caching.Memory;
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
    private readonly IProceduralMapConfigRepository _configs;
    private readonly IProceduralLayoutInputsResolver _inputsResolver;
    private readonly IMemoryCache _poolMemberCache;
    private readonly ILogger<ObservabilityService> _logger;

    public ObservabilityService(
        IReplicatedCache cache,
        IWorldRepository worlds,
        IMapTemplateRepository maps,
        IProceduralMapConfigRepository configs,
        IProceduralLayoutInputsResolver inputsResolver,
        IMemoryCache poolMemberCache,
        ILogger<ObservabilityService> logger)
    {
        _cache = cache;
        _worlds = worlds;
        _maps = maps;
        _configs = configs;
        _inputsResolver = inputsResolver;
        _poolMemberCache = poolMemberCache;
        _logger = logger;
    }

    public async Task<PagedResult<OnlinePlayerDto>> GetOnlineAsync(
        PresencePaginateFilters filters, CancellationToken ct = default)
    {
        int page = filters.Page < 1 ? 1 : filters.Page;
        int pageSize = filters.PageSize is < 1 or > 50 ? 50 : filters.PageSize;

        // Instances overwhelmingly repeat the same handful of template ids, and this loop
        // runs over every instance in every world before Skip/Take ever applies. Memoize
        // for the duration of the request so that cost is proportional to distinct
        // templates seen, not to total instance count.
        Dictionary<ushort, string> templateNames = [];

        List<OnlinePlayerDto> rows = [];
        foreach (AvalonWorld world in await _worlds.FindAllAsync(track: false, ct))
        {
            ushort worldId = world.Id.Value;
            if (filters.WorldId is { } wantWorld && worldId != wantWorld) continue;

            WorldPresenceSnapshot? snapshot = await ReadWorldAsync(worldId);
            if (snapshot is null) continue;

            foreach (InstancePresenceSnapshot instance in snapshot.Instances ?? [])
            {
                if (filters.TemplateId is { } wantTemplate && instance.TemplateId != wantTemplate) continue;

                string templateName = await TemplateNameAsync(instance.TemplateId, ct, templateNames);
                foreach (CharacterPresenceSnapshot c in instance.Characters ?? [])
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
            snapshot.Instances?.FirstOrDefault(i => i.InstanceId == index.InstanceId);
        CharacterPresenceSnapshot? target =
            instance?.Characters?.FirstOrDefault(c => c.CharacterId == characterId);
        if (instance is null || target is null) return null;

        return new PlayerPresenceDto
        {
            Target = ToDto(target),
            Instance = await ToDtoAsync(instance, index.WorldId, ct),
            LayoutStale = await IsLayoutStaleAsync(instance, ct),
            CapturedAt = snapshot.CapturedAt,
        };
    }

    public async Task<InstancePresenceDto?> GetInstancePresenceAsync(Guid instanceId, CancellationToken ct = default)
    {
        foreach (AvalonWorld world in await _worlds.FindAllAsync(track: false, ct))
        {
            WorldPresenceSnapshot? snapshot = await ReadWorldAsync(world.Id.Value);
            InstancePresenceSnapshot? instance =
                snapshot?.Instances?.FirstOrDefault(i => i.InstanceId == instanceId);
            if (instance is not null) return await ToDtoAsync(instance, world.Id.Value, ct);
        }
        return null;
    }

    /// <summary>
    /// Reads and deserialises one world's snapshot, treating anything unreadable as
    /// absent rather than throwing — an expired key, a malformed blob (<see
    /// cref="PresenceJson.Deserialize{T}"/> already returns null for those) and a
    /// recognisable-but-wrong schema version must all fail closed the same way. A snapshot
    /// stamped with a version this build does not recognise is exactly that last case: it
    /// was written by a different build (a rolling deploy, most commonly), so trusting its
    /// shape is unsafe even though it deserialised without throwing.
    /// </summary>
    private async Task<WorldPresenceSnapshot?> ReadWorldAsync(ushort worldId)
    {
        string? raw = await _cache.GetAsync(CacheKeys.WorldPresence(worldId));
        if (raw is null) return null;

        WorldPresenceSnapshot? snapshot = PresenceJson.Deserialize<WorldPresenceSnapshot>(raw);
        if (snapshot is null) return null;

        if (snapshot.Version != WorldPresenceSnapshot.CurrentVersion)
        {
            _logger.LogDebug(
                "Ignoring presence snapshot for world {WorldId}: unrecognized schema version {Version}",
                worldId, snapshot.Version);
            return null;
        }

        return snapshot;
    }

    private async Task<string> TemplateNameAsync(
        ushort templateId, CancellationToken ct, Dictionary<ushort, string>? memo = null)
    {
        if (memo is not null && memo.TryGetValue(templateId, out string? cached)) return cached;

        MapTemplate? template = await _maps.FindByIdAsync(new MapTemplateId(templateId), track: false, ct);
        string name = template?.Name ?? $"#{templateId}";

        if (memo is not null) memo[templateId] = name;
        return name;
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
        Characters = (instance.Characters ?? []).Select(ToDto).ToList(),
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
    ///
    /// <see cref="Enum.TryParse{TEnum}(string?, bool, out TEnum)"/> alone is not enough:
    /// it happily accepts numeric-looking strings, so e.g. <c>"2"</c> parses to the
    /// non-member value <c>(MapType)2</c> instead of failing. The added
    /// <see cref="Enum.IsDefined{TEnum}(TEnum)"/> check rejects that case too.
    /// </summary>
    private static MapType? ParseMapType(string mapType) =>
        Enum.TryParse(mapType, ignoreCase: true, out MapType parsed) && Enum.IsDefined(parsed)
            ? parsed
            : null;

    /// <summary>
    /// A seed only reproduces a layout while the config and chunk pool behind it are
    /// unchanged. Recompute the stamp from current DB state and compare with what the
    /// world server recorded at instance creation.
    ///
    /// An empty recorded stamp means a predefined (town) layout, which is not generated
    /// from a seed at all — absence of a stamp is not evidence of drift. Any failure to
    /// recompute is treated as "not stale": a false warning banner on every request would
    /// train admins to ignore it.
    /// </summary>
    private async Task<bool> IsLayoutStaleAsync(InstancePresenceSnapshot instance, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(instance.ConfigVersion)) return false;

        try
        {
            ProceduralMapConfig? config =
                await _configs.FindByTemplateIdAsync(new MapTemplateId(instance.TemplateId), ct);
            if (config is null) return false;

            IReadOnlyList<ChunkPoolMember>? members = await GetPoolMembersCachedAsync(config.ChunkPoolId, ct);
            if (members is null) return false;

            return !string.Equals(
                LayoutConfigVersion.Compute(config, members),
                instance.ConfigVersion,
                StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not recompute layout config version for template {TemplateId}", instance.TemplateId);
            return false;
        }
    }

    /// <summary>
    /// Pool-member resolution costs two unfiltered table reads — see
    /// <see cref="IProceduralLayoutInputsResolver"/> — and <see cref="GetPlayerPresenceAsync"/>
    /// is polled by the admin dashboard every PRESENCE_POLL_MS (1.5s), so an open player-detail
    /// view would otherwise repeat both full-table reads on every poll. Caching the resolved
    /// member list by <see cref="ChunkPoolId"/> for 30 seconds cuts that to one pair of reads
    /// per pool per 30s window regardless of how many admins are watching.
    ///
    /// Tradeoff, written down deliberately: a pool-weight or geometry edit can take up to 30s to
    /// surface as <see cref="PlayerPresenceDto.LayoutStale"/>. That is acceptable because the
    /// flag is advisory — it warns that rendered geometry may not match the player's client — not
    /// a correctness guarantee.
    ///
    /// Deliberately NOT shared with <see cref="MapService.PreviewLayoutAsync"/>: that path is
    /// admin-triggered, not polled, and an admin previewing a layout right after editing a pool
    /// must see the edit immediately, not up to 30s later.
    ///
    /// Keyed on a prefixed string, not the bare <see cref="ChunkPoolId"/>: <c>ValueObject&lt;TValue&gt;</c>
    /// compares and hashes by <c>Value</c> alone, so a bare <c>ChunkPoolId(3)</c> would collide in this
    /// shared <see cref="IMemoryCache"/> with a <c>MapTemplateId(3)</c>, a <c>SpawnTableId(3)</c>, or any
    /// other <c>ValueObject&lt;ushort&gt;</c> another feature might one day cache here.
    /// </summary>
    private async Task<IReadOnlyList<ChunkPoolMember>?> GetPoolMembersCachedAsync(ChunkPoolId poolId, CancellationToken ct)
    {
        string cacheKey = $"obs:poolMembers:{poolId.Value}";
        if (_poolMemberCache.TryGetValue(cacheKey, out IReadOnlyList<ChunkPoolMember>? cached)) return cached;

        ChunkPool? pool = await _inputsResolver.FindPoolAsync(poolId, ct);
        if (pool is null) return null;

        ProceduralPoolResolution resolution = await _inputsResolver.ResolveMembersAsync(pool, ct);
        _poolMemberCache.Set(cacheKey, resolution.Members, TimeSpan.FromSeconds(30));
        return resolution.Members;
    }
}
