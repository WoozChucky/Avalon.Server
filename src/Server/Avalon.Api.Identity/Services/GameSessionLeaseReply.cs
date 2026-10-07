using Avalon.Common.GameAuth;

// The published OpenAPI document names this schema by the type's full name, so the type keeps the namespace it had
// before the split (#794).
namespace Avalon.Api.Services;

public sealed record GameSessionLeaseReply
{
    public string State { get; init; } = GameAuthStates.Pending;
    public string? Error { get; init; }
    public string? AccountId { get; init; }
    public string? GameSessionId { get; init; }
    public string? GameContextId { get; init; }
    public string? FencingToken { get; init; }
    public string? ServerId { get; init; }
    public ushort? WorldId { get; init; }
    public ushort? AccessLevel { get; init; }
    public int? CredentialsVersion { get; init; }
    public string? SessionEpoch { get; init; }
    public DateTime? LeaseUntil { get; init; }
    public DateTime? AuthorizationUntil { get; init; }
    public static GameSessionLeaseReply Failure(string error) => new() { Error = error };
}
