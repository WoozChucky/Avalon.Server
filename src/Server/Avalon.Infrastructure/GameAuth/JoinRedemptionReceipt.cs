namespace Avalon.Infrastructure.GameAuth;

/// <summary>Authority for one authenticated server/connection. Pending authority cannot admit gameplay.</summary>
public sealed record JoinRedemptionReceipt
{
    public string State { get; init; } = "pending";
    public string? Error { get; init; }
    public string? AccountId { get; init; }
    public string? GameSessionId { get; init; }
    public string? GameContextId { get; init; }
    public string? FencingToken { get; init; }
    public string? ConnectionId { get; init; }
    public string? RedemptionId { get; init; }
    public string? ServerId { get; init; }
    public ushort? WorldId { get; init; }
    public uint? CharacterId { get; init; }
    public int CredentialsVersion { get; init; }
    public string? SessionEpoch { get; init; }
    public DateTime? LeaseUntil { get; init; }
    public DateTime? AuthorizationUntil { get; init; }
    public static JoinRedemptionReceipt Failure(string error) => new() { Error = error };
}
