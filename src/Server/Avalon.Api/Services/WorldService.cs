using Avalon.Api.Contract;
using Avalon.Api.Exceptions;
using Avalon.Database;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Extensions;
using Avalon.Common.Accounts;
using Avalon.Domain.Auth;
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
        CancellationToken cancellationToken = default);

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

    public WorldService(IWorldRepository repository)
    {
        _repository = repository;
    }

    public async Task<PagedResult<WorldDto>> ListAsync(AccountAccessLevel caller, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        var filters = new WorldPaginateFilters
        {
            Page = page < 1 ? 1 : page,
            PageSize = pageSize is < 1 or > 50 ? 50 : pageSize,
            CallerAccessLevel = caller,
        };

        var result = await _repository.PaginateAsync(filters, track: false, cancellationToken);
        return result.MapTo(ToDto);
    }

    public async Task<WorldDto?> GetAsync(ushort id, AccountAccessLevel caller,
        CancellationToken cancellationToken = default)
    {
        var world = await _repository.FindByIdAsync(new WorldId(id), track: false, cancellationToken);

        // Same rule as the TCP world list. A world the caller may not enter reads as missing.
        if (world is null || !AccessLevels.ForWorld(world.AccessLevelRequired).Allows(caller))
            return null;

        return ToDto(world);
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
            Status = (Avalon.Domain.Auth.WorldStatus)request.Status,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var created = await _repository.CreateAsync(world, cancellationToken);
        return ToDto(created);
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
        if (request.Status.HasValue) world.Status = (Avalon.Domain.Auth.WorldStatus)request.Status.Value;

        world.UpdatedAt = DateTime.UtcNow;

        await _repository.UpdateAsync(world, cancellationToken);
        return ToDto(world);
    }

    private static WorldDto ToDto(WorldEntity w) => new()
    {
        Id = w.Id.Value,
        Name = w.Name,
        Type = (Avalon.Api.Contract.WorldType)w.Type,
        AccessLevelRequired = (Avalon.Api.Contract.AccountAccessLevel)w.AccessLevelRequired,
        Host = w.Host,
        Port = w.Port,
        MinVersion = w.MinVersion,
        Version = w.Version,
        Status = (Avalon.Api.Contract.WorldStatus)w.Status,
        CreatedAt = w.CreatedAt,
        UpdatedAt = w.UpdatedAt,
        OnlineCount = 0,
    };
}
