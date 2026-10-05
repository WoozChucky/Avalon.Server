using Avalon.Api.Worlds;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.GameAuth;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Services;

/// <summary>Uses current access, maintenance and readiness plus deployment certificate identity.</summary>
public sealed class GameServerAllocator(IWorldRepository worlds, IAccountRepository accounts, IWorldDatabases databases,
    IWorldReadiness readiness, IWorldRepositories repositories, IOptions<GameWorkloadConfiguration> workloads, TimeProvider clock,
    GameApplicationAccessPolicy applications) : IGameServerAllocator
{
    public async Task<IReadOnlyList<GameWorldDestination>> ListAsync(GameContextRecord context, CancellationToken cancellationToken)
    {
        var result = new List<GameWorldDestination>();
        foreach (var definition in workloads.Value.Servers)
        {
            var destination = await FindAsync(context, definition.WorldId, null, cancellationToken);
            if (destination is not null) result.Add(destination);
        }
        return result.OrderBy(x => x.WorldId).ToArray();
    }
    public async Task<GameWorldDestination?> FindAsync(GameContextRecord context, ushort worldId, uint? characterId, CancellationToken cancellationToken)
    {
        if (!applications.AllowsWorld(context.ApplicationKey, worldId) || context.AccountId is not { } id || context.ProtocolVersion != GameWorkloadConfiguration.ClientProtocolVersion || characterId == 0) return null;
        var server = workloads.Value.Servers.SingleOrDefault(s => s.WorldId == worldId);
        if (server is null || !databases.IsAvailable(new WorldId(worldId))) return null;
        var root = await accounts.FindByIdAsync(new AccountId(id), false, cancellationToken);
        if (root is null || root.Status != AccountStatus.Active || root.IsLockedAt(clock.GetUtcNow().UtcDateTime) ||
            root.CredentialsVersion != context.CredentialsVersion || root.SessionEpoch != context.SessionEpoch) return null;
        var world = await worlds.FindByIdAsync(new WorldId(worldId), false, cancellationToken);
        if (world is null || !applications.AllowsWorldAccess(context.ApplicationKey, worldId, world.AccessLevelRequired, root.AccessLevel) ||
            world.Port is < 1 or > 65535 || Uri.CheckHostName(world.Host) == UriHostNameType.Unknown ||
            !await readiness.IsReadyAsync(worldId, cancellationToken)) return null;
        if (new WorldMaintenanceState(world.MaintenanceEnabled, world.MaintenanceRevision, world.MaintenanceDeadlineUtc)
                .IsCutoffActive(clock.GetUtcNow().UtcDateTime) && (root.AccessLevel & AccountAccessLevel.Admin) == 0) return null;
        if (characterId is { } selected && await repositories.Characters(new WorldId(worldId))
            .FindByIdAndAccountAsync(new CharacterId(selected), root.Id, cancellationToken) is null) return null;
        return new(worldId, server.ServerId, world.Name, world.Host, world.Port, server.TlsServerName,
            server.TlsCertificateSha256, world.MinVersion, world.Version);
    }
}
