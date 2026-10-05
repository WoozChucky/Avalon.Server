using Avalon.Common.GameAuth;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class GameProviderAttemptRequest
{
    [Required, StringLength(128)] public required string ApplicationKey { get; init; }
    [Required, StringLength(32)] public required string ProtocolVersion { get; init; }
    public Guid ClientRunId { get; init; }
    [Required, StringLength(GameAuthPolicy.TokenCharacters, MinimumLength = GameAuthPolicy.TokenCharacters)] public required string LinkChallenge { get; init; }
    [StringLength(GameAuthPolicy.TokenCharacters, MinimumLength = GameAuthPolicy.TokenCharacters)] public string? GameContextCredential { get; init; }
}
public sealed record ProviderAuthAttemptReply(string AttemptCredential, string ExpectedChallenge, DateTime ExpiresAt)
{
    public override string ToString() => "Provider authentication attempt (credential redacted)";
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class GameProviderProofRequest
{
    [Required, StringLength(32)] public required string Provider { get; init; }
    [Required, StringLength(GameAuthPolicy.TokenCharacters, MinimumLength = GameAuthPolicy.TokenCharacters)] public required string AttemptCredential { get; init; }
    [Required, StringLength(GameAuthPolicy.MaximumBodyBytes / 2)] public required string Proof { get; init; }
    public override string ToString() => "Provider authentication proof (credentials redacted)";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class GameHandoffRequest
{
    [Required, StringLength(GameAuthPolicy.TokenCharacters, MinimumLength = GameAuthPolicy.TokenCharacters)] public required string AttemptCredential { get; init; }
    [Required, StringLength(GameAuthPolicy.TokenCharacters, MinimumLength = GameAuthPolicy.TokenCharacters)] public required string HandoffTicket { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class GameContextRefreshRequest
{
    [Required, StringLength(GameAuthPolicy.TokenCharacters, MinimumLength = GameAuthPolicy.TokenCharacters)] public required string GameContextRefreshToken { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class GameContextCredentialRequest
{
    [Required, StringLength(GameAuthPolicy.TokenCharacters, MinimumLength = GameAuthPolicy.TokenCharacters)] public required string GameContextCredential { get; init; }
}
