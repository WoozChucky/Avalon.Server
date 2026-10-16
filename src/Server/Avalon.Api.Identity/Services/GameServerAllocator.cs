using System.Collections.Concurrent;
using Avalon.Api.Hosting.Worlds;
using Avalon.Common.Accounts;
using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.GameAuth;
using Avalon.Infrastructure.WorldPlaces;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Identity.Services;

/// <summary>
/// Uses current access, maintenance and readiness plus deployment certificate identity, and says why it refuses a world
/// (#1053, <see cref="GameWorldRefusal"/>): a refusal a retry may change (<see cref="GameWorldRefusal.Unavailable"/>) is
/// told apart from one it never will, so a client stops retrying the second.
/// </summary>
public sealed partial class GameServerAllocator(IWorldRepository worlds, IAccountRepository accounts, IWorldDatabases databases,
    IWorldReadiness readiness, IWorldRepositories repositories, IOptions<GameWorkloadConfiguration> workloads, TimeProvider clock,
    GameApplicationAccessPolicy applications, ILogger<GameServerAllocator> logger, IWorldOccupancy occupancy) : IGameServerAllocator
{
    /// <summary>The unreadable <c>MinVersion</c> last reported per world, so each is logged once, not per request.</summary>
    private static readonly ConcurrentDictionary<ushort, string> s_unreadableMinVersions = new();

    public async Task<IReadOnlyList<GameWorldDestination>> ListAsync(GameContextRecord context, CancellationToken cancellationToken)
    {
        // The context and its account are read once for the whole list, not once per world (#1053).
        Eligibility eligibility = await EligibilityAsync(context, cancellationToken);
        if (eligibility.Account is not { } root) return [];
        var result = new List<GameWorldDestination>();
        foreach (GameServerDefinition definition in workloads.Value.Servers)
        {
            Checked world = await CheckWorldAsync(context, root, definition.WorldId, null, cancellationToken);
            // A world the client is only too old for stays listed, marked, so the player is told to update (#1053).
            if ((world.Allocation.Destination ?? world.Outdated) is not { } destination) continue;
            // Places taken and the line's length, the ones marked RequiresUpdate included (#967).
            WorldOccupancy counts = await occupancy.ReadAsync(definition.WorldId, cancellationToken);
            result.Add(destination with { PlayerCount = counts.PlayerCount, QueueLength = counts.QueueLength });
        }
        return result.OrderBy(x => x.WorldId).ToArray();
    }

    public async Task<GameWorldAllocation> FindAsync(GameContextRecord context, ushort worldId, uint? characterId, CancellationToken cancellationToken)
    {
        Eligibility eligibility = await EligibilityAsync(context, cancellationToken);
        return eligibility.Account is { } root
            ? (await CheckWorldAsync(context, root, worldId, characterId, cancellationToken)).Allocation
            : GameWorldAllocation.Refuse(eligibility.Refusal);
    }

    /// <summary>
    /// What no world changes: the context names an account, at or above the API's floor, which still stands behind it
    /// (its credentials version, then its standing, then the epoch). The account when it may play, otherwise why not.
    /// </summary>
    private async Task<Eligibility> EligibilityAsync(GameContextRecord context, CancellationToken cancellationToken)
    {
        if (context.AccountId is not { } id) return new(null, GameWorldRefusal.ContextStale);
        if (!workloads.Value.Admits(context.ProtocolVersion)) return new(null, GameWorldRefusal.UnsupportedProtocol);
        Account? root = await accounts.FindByIdAsync(new AccountId(id), false, cancellationToken);
        // The standing is named only to a context its account still stands behind (its credentials version), and before
        // the epoch, which a ban or a consolidation moves itself (#882). The password lock is not asked: it guards the
        // password steps only, never an identity proven another way.
        if (root is null || root.CredentialsVersion != context.CredentialsVersion) return new(null, GameWorldRefusal.ContextStale);
        if (GameAccountStanding.Refusal(root) is { } standing) return new(null, Standing(standing));
        if (root.Status != AccountStatus.Active || root.SessionEpoch != context.SessionEpoch) return new(null, GameWorldRefusal.ContextStale);
        return new(root, default);
    }

    /// <summary>
    /// One world for an eligible account (<see cref="EligibilityAsync"/>): the allocation, and on
    /// <see cref="GameWorldRefusal.ClientOutdated"/> the destination the client would have been given had it been new
    /// enough (<see cref="Checked.Outdated"/>, marked): the version is checked after every other rule but the
    /// character's, so a world refused for it alone is one the context is otherwise eligible for.
    /// </summary>
    private async Task<Checked> CheckWorldAsync(GameContextRecord context, Account root, ushort worldId, uint? characterId,
        CancellationToken cancellationToken)
    {
        if (!applications.AllowsWorld(context.ApplicationKey, worldId)) return Refuse(GameWorldRefusal.NotAllowed);
        if (characterId == 0) return Refuse(GameWorldRefusal.CharacterNotFound);

        // A world the context may not enter is not revealed, even while it is unavailable (the rule of
        // WorldRouteMiddleware): no row, no workload and an access level too high are one answer, after the same reads
        // (the account's and the world's row), and before anything about the world's state is read (#1053).
        Domain.Auth.World? world = await worlds.FindByIdAsync(new WorldId(worldId), false, cancellationToken);
        GameServerDefinition? server = workloads.Value.Servers.SingleOrDefault(s => s.WorldId == worldId);
        if (world is null || server is null ||
            !applications.AllowsWorldAccess(context.ApplicationKey, worldId, world.AccessLevelRequired, root.AccessLevel))
        {
            return Refuse(GameWorldRefusal.NotAllowed);
        }

        // Only a world the context may enter says it is down.
        if (!databases.IsAvailable(new WorldId(worldId)) || world.Port is < 1 or > 65535 ||
            Uri.CheckHostName(world.Host) == UriHostNameType.Unknown || !await readiness.IsReadyAsync(worldId, cancellationToken))
        {
            return Refuse(GameWorldRefusal.Unavailable);
        }

        bool admin = AccountAccessLevel.Admin.Allows(root.AccessLevel);
        bool cutoff = new WorldMaintenanceState(world.MaintenanceEnabled, world.MaintenanceRevision, world.MaintenanceDeadlineUtc)
            .IsCutoffActive(clock.GetUtcNow().UtcDateTime);
        if (cutoff && !admin)
        {
            return Refuse(GameWorldRefusal.Unavailable);
        }

        // The world's own floor (#1053), read as strictly as the API's: one that does not read is the server's
        // misconfiguration, not the client's fault, so the world is unavailable rather than the client outdated.
        if (!ClientVersionFloor.TryParse(world.MinVersion, out uint required))
        {
            ReportUnreadableMinVersion(worldId, world.MinVersion);
            return Refuse(GameWorldRefusal.Unavailable);
        }

        var destination = new GameWorldDestination(worldId, server.ServerId, world.Name, world.Host, world.Port, server.TlsServerName,
            server.TlsCertificateSha256, world.MinVersion, world.Version,
            Status: (cutoff ? WorldStatus.Maintenance : WorldStatus.Online).ToString(), PlayerLimit: world.PlayerLimit,
            Staff: admin);
        // The API floor above has already read the context's version.
        if (!ClientVersionFloor.TryParse(context.ProtocolVersion, out uint version) || version < required)
            return new(GameWorldAllocation.Outdated(ClientVersionFloor.Format(required)), destination with { RequiresUpdate = true });

        if (characterId is { } selected && await repositories.Characters(new WorldId(worldId))
            .FindByIdAndAccountAsync(new CharacterId(selected), root.Id, cancellationToken) is null)
        {
            return Refuse(GameWorldRefusal.CharacterNotFound);
        }

        return new(GameWorldAllocation.Admit(destination));
    }

    private static Checked Refuse(GameWorldRefusal refusal) => new(GameWorldAllocation.Refuse(refusal));

    private static GameWorldRefusal Standing(string refusal) => refusal switch
    {
        GameAuthErrors.AccountBanned => GameWorldRefusal.AccountBanned,
        GameAuthErrors.AccountDeactivated => GameWorldRefusal.AccountDeactivated,
        _ => GameWorldRefusal.AccountConsolidating,
    };

    private void ReportUnreadableMinVersion(ushort worldId, string? minVersion)
    {
        string value = minVersion ?? string.Empty;
        if (s_unreadableMinVersions.TryGetValue(worldId, out string? reported) && reported == value) return;
        s_unreadableMinVersions[worldId] = value;
        Log.UnreadableMinVersion(logger, worldId);
    }

    private readonly record struct Checked(GameWorldAllocation Allocation, GameWorldDestination? Outdated = null);

    private readonly record struct Eligibility(Account? Account, GameWorldRefusal Refusal);

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error,
            Message = "World {WorldId}'s MinVersion is not a version of three whole numbers (major.minor.patch); no join ticket is issued for it until it is fixed")]
        public static partial void UnreadableMinVersion(ILogger logger, ushort worldId);
    }
}
