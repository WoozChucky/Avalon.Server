namespace Avalon.Infrastructure.GameAuth;

public sealed record GameWorldDestination(ushort WorldId, string ServerId, string Name, string Host, int Port,
    string TlsServerName, string TlsCertificateSha256, string MinVersion, string Version);

public interface IGameServerAllocator
{
    Task<IReadOnlyList<GameWorldDestination>> ListAsync(GameContextRecord context, CancellationToken cancellationToken);
    Task<GameWorldDestination?> FindAsync(GameContextRecord context, ushort worldId, uint? characterId, CancellationToken cancellationToken);
}

public sealed record GameJoinReply(string? Error = null, string? JoinTicket = null, DateTime? ExpiresAt = null,
    GameWorldDestination? Destination = null)
{
    public override string ToString() => $"Game admission ticket, error: {Error} (ticket redacted)";
}

internal sealed record JoinIssueReceipt(string Binding, string Envelope);
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
    public DateTime ExpiresAt { get; init; }
    public DateTime AuthorizationUntil { get; init; }
    public string? Binding { get; init; }
    public DateTime? WorkerUntil { get; init; }
    public string? Receipt { get; init; }
    public DateTime? ReceiptExpiresAt { get; init; }
}
