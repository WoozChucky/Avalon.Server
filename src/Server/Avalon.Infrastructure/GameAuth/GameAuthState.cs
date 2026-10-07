using System.Text.Json;
using Avalon.Common.GameAuth;

namespace Avalon.Infrastructure.GameAuth;

public sealed record AuthAttemptReply(string AttemptCredential, string ExpectedSteamIdentity, DateTime ExpiresAt)
{
    public override string ToString() => "Game authentication attempt (credential redacted)";
}

public sealed record GameAuthReply
{
    public required string State { get; init; }
    public string? Error { get; init; }
    public string? AccountId { get; init; }
    public string? GameContextCredential { get; init; }
    public string? GameContextRefreshToken { get; init; }
    public DateTime? ContextExpiresAt { get; init; }
    public DateTime? AuthorizationValidUntil { get; init; }
    public string? LicenseSource { get; init; }
    public string? PendingLinkId { get; init; }
    public string? NextAction { get; init; }
    public override string ToString() => $"Game authentication state: {State}, error: {Error} (credentials redacted)";
    public static GameAuthReply Failure(string error) => new() { State = GameAuthStates.PendingIdentity, Error = error };
}

public sealed record GameContextRecord
{
    public Guid Id { get; init; }
    public Guid ClientRunId { get; init; }
    public uint SteamAppId { get; init; }
    public string? ApplicationKey { get; init; }
    public Guid? LicenseId { get; init; }
    public long? LicenseRevision { get; init; }
    public DateTime? IdentityValidUntil { get; init; }
    public required string ProtocolVersion { get; init; }
    public required string Environment { get; init; }
    public string Audience { get; init; } = GameAuthPolicy.ContextAudience;
    public string Product { get; init; } = Avalon.Configuration.StoreAuthenticationConfiguration.Product;
    public required string State { get; init; }
    public long? AccountId { get; init; }
    public int CredentialsVersion { get; init; }
    public long SessionEpoch { get; init; }
    public Guid? LauncherFamilyId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime AbsoluteExpiresAt { get; init; }
    public DateTime CredentialExpiresAt { get; init; }
    public int Generation { get; init; }
    public required string CredentialDigest { get; init; }
    public required string RefreshDigest { get; init; }
    public string? Provider { get; init; }
    public string? ProviderSubject { get; init; }
    public DateTime? IdentityVerifiedAt { get; init; }
    public DateTime? AuthorizationValidUntil { get; init; }
    public Guid? LicenseObservationId { get; init; }
    public Guid? PendingLinkId { get; init; }
    public string? LinkChallenge { get; init; }
    public DateTime? LinkProofExpiresAt { get; init; }
    public Guid? ActiveGameSessionId { get; init; }
}

public sealed record GameAuthTokenRecord(Guid ContextId, string Kind, int Generation, bool Spent = false,
    Guid? RequestId = null, string? Receipt = null, DateTime? ReceiptExpiresAt = null);

public sealed record AuthAttemptRecord
{
    public Guid Id { get; init; }
    public Guid ClientRunId { get; init; }
    public uint SteamAppId { get; init; }
    public string? ApplicationKey { get; init; }
    public string? ProviderChallenge { get; init; }
    public required string Channel { get; init; }
    public required string ProtocolVersion { get; init; }
    public required string ExpectedSteamIdentity { get; init; }
    public required string LinkChallenge { get; init; }
    public Guid? ContextId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public string? Binding { get; init; }
    public Guid? WorkerId { get; init; }
    public DateTime? WorkerUntil { get; init; }
    public int Claims { get; init; }
    public string? HandoffGrant { get; init; }
    public string? Receipt { get; init; }
    public DateTime? ReceiptExpiresAt { get; init; }
}

public static class GameAuthJson
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = GameAuthPolicy.MaximumJsonDepth };
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T? Deserialize<T>(string? value) where T : class => value is null ? null : JsonSerializer.Deserialize<T>(value, Options);
}
