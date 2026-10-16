using System.Text.Json.Serialization;
using Avalon.Common.GameAuth;

namespace Avalon.Infrastructure.GameAuth;

/// <summary>
/// A world a game context may enter, as the world list and a join ticket name it. <see cref="RequiresUpdate"/> is true
/// only in the world list, on a world the client is too old for and otherwise eligible for (#1053): the player is told
/// to update to <see cref="MinVersion"/>, and a join ticket for it is refused <c>CLIENT_OUTDATED</c>.
/// <see cref="Status"/>, <see cref="PlayerCount"/>, <see cref="PlayerLimit"/> and <see cref="QueueLength"/> are the world
/// list's (#967); <see cref="Staff"/> (the Admin flag) is for the join's capacity step and never leaves the server.
/// </summary>
public sealed record GameWorldDestination(ushort WorldId, string ServerId, string Name, string Host, int Port,
    string TlsServerName, string TlsCertificateSha256, string MinVersion, string Version, bool RequiresUpdate = false,
    string? Status = null, int? PlayerCount = null, int? PlayerLimit = null, int? QueueLength = null,
    [property: JsonIgnore] bool Staff = false);

public interface IGameServerAllocator
{
    /// <summary>The worlds the context may enter, with those it is only too old for marked <see cref="GameWorldDestination.RequiresUpdate"/>.</summary>
    Task<IReadOnlyList<GameWorldDestination>> ListAsync(GameContextRecord context, CancellationToken cancellationToken);
    Task<GameWorldAllocation> FindAsync(GameContextRecord context, ushort worldId, uint? characterId, CancellationToken cancellationToken);
}

/// <summary>
/// Why the allocator refused a world (#1053). <see cref="Unavailable"/> is the only one a retry may change; every other
/// is permanent for the context as it stands. The default, so an allocation nothing set is a refusal.
/// </summary>
public enum GameWorldRefusal : byte
{
    /// <summary>Try later, told only of a world the context may enter: its database unavailable, its host or port unusable, not ready (no fresh heartbeat), the maintenance cutoff in force for a non-Admin, or a <c>MinVersion</c> that does not read.</summary>
    Unavailable = 0,
    /// <summary>The context's client version is below the world's <c>MinVersion</c>.</summary>
    ClientOutdated,
    /// <summary>The context's client version is below the API's floor, <c>Application:GameWorkloads:MinClientVersion</c> (#949).</summary>
    UnsupportedProtocol,
    /// <summary>The context's application is not allowed on the world, the account's access level is below the world's, or the world has no row or no workload: one reason for all, so a world the context may not enter is never told apart from one that does not exist.</summary>
    NotAllowed,
    /// <summary>The account is banned (named only at the context's credentials version, #882).</summary>
    AccountBanned,
    /// <summary>The account is deactivated (named only at the context's credentials version, #882).</summary>
    AccountDeactivated,
    /// <summary>The account is being consolidated (named only at the context's credentials version, #882).</summary>
    AccountConsolidating,
    /// <summary>The context no longer stands for its account: no account, its credentials version or session epoch moved, or no account on the context.</summary>
    ContextStale,
    /// <summary>The character asked for does not exist on the world or is not the account's.</summary>
    CharacterNotFound,
}

/// <summary>
/// The allocator's answer (#1053): <see cref="Destination"/> when the world may be entered, otherwise null and
/// <see cref="Refusal"/> says why, with <see cref="RequiredVersion"/> (the world's <c>MinVersion</c>, canonical) on
/// <see cref="GameWorldRefusal.ClientOutdated"/>.
/// </summary>
public readonly record struct GameWorldAllocation
{
    private GameWorldAllocation(GameWorldDestination? destination, GameWorldRefusal refusal, string? requiredVersion)
    {
        Destination = destination;
        Refusal = refusal;
        RequiredVersion = requiredVersion;
    }

    public GameWorldDestination? Destination { get; }
    public GameWorldRefusal Refusal { get; }
    public string? RequiredVersion { get; }

    public static GameWorldAllocation Admit(GameWorldDestination destination) => new(destination, default, null);
    public static GameWorldAllocation Refuse(GameWorldRefusal refusal) => new(null, refusal, null);
    public static GameWorldAllocation Outdated(string requiredVersion) => new(null, GameWorldRefusal.ClientOutdated, requiredVersion);

    /// <summary>The error a join or reconnect ticket's issue answers for <see cref="Refusal"/>, the REST client's.</summary>
    public string IssueError => Refusal switch
    {
        GameWorldRefusal.ContextStale => GameAuthErrors.AuthorizationRequired,
        _ => SharedError,
    };

    /// <summary>The error a join ticket's redemption answers the world for <see cref="Refusal"/>.</summary>
    public string RedemptionError => Refusal switch
    {
        GameWorldRefusal.ContextStale => GameAuthErrors.ContextRevoked,
        _ => SharedError,
    };

    private string SharedError => Refusal switch
    {
        GameWorldRefusal.ClientOutdated => GameAuthErrors.ClientOutdated,
        GameWorldRefusal.UnsupportedProtocol => GameAuthErrors.UnsupportedProtocol,
        GameWorldRefusal.NotAllowed => GameAuthErrors.WorldNotAllowed,
        GameWorldRefusal.AccountBanned => GameAuthErrors.AccountBanned,
        GameWorldRefusal.AccountDeactivated => GameAuthErrors.AccountDeactivated,
        GameWorldRefusal.AccountConsolidating => GameAuthErrors.AccountConsolidating,
        GameWorldRefusal.CharacterNotFound => GameAuthErrors.CharacterNotFound,
        _ => GameAuthErrors.WorldUnavailable,
    };
}

/// <summary>
/// A join or reconnect ticket, or why none was issued (<see cref="Error"/>), or (#967) the account's place in a full
/// world's line, with when to check in (<see cref="Position"/> from 1, <see cref="CheckInAfterSeconds"/>; no ticket, no
/// error). <see cref="RequiredVersion"/> is set only with <c>CLIENT_OUTDATED</c> (#1053): the oldest client version the
/// world accepts.
/// </summary>
public sealed record GameJoinReply(string? Error = null, string? JoinTicket = null, DateTime? ExpiresAt = null,
    GameWorldDestination? Destination = null, string? RequiredVersion = null, int? Position = null,
    int? CheckInAfterSeconds = null)
{
    public override string ToString() => $"Game admission ticket, error: {Error} (ticket redacted)";
}

/// <summary>
/// A join ticket's issue, kept for its idempotent replay. <see cref="Ticket"/> is the ticket's id in the place ledger (its
/// grant's <c>GameSessionId</c>, #967), so a replay re-enters the place as that ticket's; null on a receipt written before.
/// </summary>
internal sealed record JoinIssueReceipt(string Binding, string Envelope, Guid? Ticket = null);
internal sealed record JoinTicketGrant
{
    public Guid TicketId { get; init; }
    public Guid GameSessionId { get; init; }
    public Guid ContextId { get; init; }
    public int ContextGeneration { get; init; }
    public long AccountId { get; init; }
    public int CredentialsVersion { get; init; }
    public long SessionEpoch { get; init; }
    public required string ServerId { get; init; }
    public ushort WorldId { get; init; }
    public uint? CharacterId { get; init; }
    public required string Environment { get; init; }
    public long ExpectedFence { get; init; }
    public bool Takeover { get; init; }
    /// <summary>A reconnect ticket: it may replace an active session, never a pending one (#1013).</summary>
    public bool Reconnect { get; init; }
    public DateTime ExpiresAt { get; init; }
    public DateTime AuthorizationUntil { get; init; }
    public string? Binding { get; init; }
    public DateTime? WorkerUntil { get; init; }
    public string? Receipt { get; init; }
    public DateTime? ReceiptExpiresAt { get; init; }
}
