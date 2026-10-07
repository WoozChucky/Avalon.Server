using Avalon.Api.Contract;
using Avalon.Api.Exceptions;
using Avalon.Api.Worlds;
using Avalon.Common.Accounts;
using Avalon.Database;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Extensions;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;
using WorldEntity = Avalon.Domain.Auth.World;

namespace Avalon.Api.Services;

public interface IWorldService
{
    /// <summary>
    /// A page of the worlds <paramref name="caller"/> may enter; paging and the total count cover
    /// only those (#452).
    /// </summary>
    Task<PagedResult<WorldDto>> ListAsync(AccountAccessLevel caller, int page, int pageSize,
        CancellationToken cancellationToken = default, string? sortBy = null,
        SortDirection sortDirection = SortDirection.Ascending);

    /// <summary>
    /// The world, or null when it does not exist or <paramref name="caller"/> may not enter it. The
    /// two are indistinguishable on purpose, so a lookup does not reveal a hidden world (#452).
    /// </summary>
    Task<WorldDto?> GetAsync(ushort id, AccountAccessLevel caller, CancellationToken cancellationToken = default);

    Task<WorldDto> CreateAsync(CreateWorldRequest request, CancellationToken cancellationToken = default);
    Task<WorldDto?> UpdateAsync(ushort id, UpdateWorldRequest request, CancellationToken cancellationToken = default);
}

public class WorldService : IWorldService
{
    private readonly IWorldRepository _repository;
    private readonly IWorldDatabases _databases;
    private readonly IWorldReadiness _readiness;
    private readonly TimeProvider _time;

    public WorldService(IWorldRepository repository, IWorldDatabases databases, IWorldReadiness readiness,
        TimeProvider? time = null)
    {
        _repository = repository;
        _databases = databases;
        _readiness = readiness;
        _time = time ?? TimeProvider.System;
    }

    public async Task<PagedResult<WorldDto>> ListAsync(AccountAccessLevel caller, int page, int pageSize,
        CancellationToken cancellationToken = default, string? sortBy = null,
        SortDirection sortDirection = SortDirection.Ascending)
    {
        var filters = new WorldPaginateFilters
        {
            Page = page < 1 ? 1 : page,
            PageSize = pageSize is < 1 or > 50 ? 50 : pageSize,
            CallerAccessLevel = caller,
            SortBy = sortBy,
        };

        if (string.Equals(sortBy, "status", StringComparison.OrdinalIgnoreCase))
        {
            // Status is derived from Redis and maintenance intent, so it cannot be sorted in SQL.
            // Sort the complete visible set before applying the requested page.
            var visible = await _repository.FindByAsync(filters.GetFilter(), cancellationToken);
            DateTime nowUtc = _time.GetUtcNow().UtcDateTime;
            var allDtos = await Task.WhenAll(visible.Select(w => ToDtoAsync(w, cancellationToken, nowUtc)));
            var sorted = sortDirection == SortDirection.Ascending
                ? allDtos.OrderBy(w => w.Status).ThenBy(w => w.Id)
                : allDtos.OrderByDescending(w => w.Status).ThenBy(w => w.Id);
            return new PagedResult<WorldDto>(filters.Page, filters.PageSize, allDtos.Length,
                sorted.Skip((filters.Page - 1) * filters.PageSize).Take(filters.PageSize).ToList());
        }

        var result = await _repository.PaginateAsync(filters, track: false, cancellationToken);
        DateTime pageNowUtc = _time.GetUtcNow().UtcDateTime;
        var items = await Task.WhenAll(result.Items.Select(w => ToDtoAsync(w, cancellationToken, pageNowUtc)));
        return new PagedResult<WorldDto>(result.Page, result.PageSize, result.TotalCount, items.ToList());
    }

    public async Task<WorldDto?> GetAsync(ushort id, AccountAccessLevel caller,
        CancellationToken cancellationToken = default)
    {
        var world = await _repository.FindByIdAsync(new WorldId(id), track: false, cancellationToken);

        // Same rule as the TCP world list. A world the caller may not enter reads as missing.
        if (world is null || !AccessLevels.ForWorld(world.AccessLevelRequired).Allows(caller))
            return null;

        return await ToDtoAsync(world, cancellationToken, _time.GetUtcNow().UtcDateTime);
    }

    public async Task<WorldDto> CreateAsync(CreateWorldRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new BusinessException("Name is required");
        if (string.IsNullOrWhiteSpace(request.Host))
            throw new BusinessException("Host is required");

        var now = DateTime.UtcNow;
        var world = new WorldEntity
        {
            Name = request.Name,
            Host = request.Host,
            Port = request.Port,
            MinVersion = request.MinVersion,
            Version = request.Version,
            Type = (Avalon.Domain.Auth.WorldType)request.Type,
            AccessLevelRequired = (Avalon.Common.Accounts.AccountAccessLevel)request.AccessLevelRequired,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var created = await _repository.CreateAsync(world, cancellationToken);
        return await ToDtoAsync(created, cancellationToken, _time.GetUtcNow().UtcDateTime);
    }

    public async Task<WorldDto?> UpdateAsync(ushort id, UpdateWorldRequest request, CancellationToken cancellationToken = default)
    {
        var world = await _repository.FindByIdAsync(new WorldId(id), track: true, cancellationToken);
        if (world is null) return null;

        if (request.Name is not null) world.Name = request.Name;
        if (request.Host is not null) world.Host = request.Host;
        if (request.Port.HasValue) world.Port = request.Port.Value;
        if (request.MinVersion is not null) world.MinVersion = request.MinVersion;
        if (request.Version is not null) world.Version = request.Version;
        if (request.Type.HasValue) world.Type = (Avalon.Domain.Auth.WorldType)request.Type.Value;
        if (request.AccessLevelRequired.HasValue) world.AccessLevelRequired = (Avalon.Common.Accounts.AccountAccessLevel)request.AccessLevelRequired.Value;

        world.UpdatedAt = DateTime.UtcNow;

        await _repository.UpdateMetadataAsync(world, cancellationToken);
        return await ToDtoAsync(world, cancellationToken, _time.GetUtcNow().UtcDateTime);
    }

    private async Task<WorldDto> ToDtoAsync(WorldEntity w, CancellationToken ct, DateTime nowUtc)
    {
        bool ready = await _readiness.IsReadyAsync(w.Id.Value, ct);
        var state = new WorldMaintenanceState(w.MaintenanceEnabled, w.MaintenanceRevision,
            w.MaintenanceDeadlineUtc);
        return new WorldDto
        {
            Id = w.Id.Value,
            Name = w.Name,
            Type = (Avalon.Api.Contract.WorldType)w.Type,
            AccessLevelRequired = (Avalon.Api.Contract.AccountAccessLevel)w.AccessLevelRequired,
            Host = w.Host,
            Port = w.Port,
            MinVersion = w.MinVersion,
            Version = w.Version,
            Status = (Avalon.Api.Contract.WorldStatus)WorldReadiness.Resolve(state, ready, nowUtc),
            Ready = ready,
            CreatedAt = w.CreatedAt,
            UpdatedAt = w.UpdatedAt,
            OnlineCount = 0,
            Configured = _databases.TryGet(w.Id, out _),
            Available = _databases.IsAvailable(w.Id),
        };
    }
}
