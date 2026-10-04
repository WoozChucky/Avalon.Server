using Avalon.Common.GameAuth;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class GameAttemptRequest
{
    [Required, StringLength(16)] public required string ChannelHint { get; init; }
    [Required, StringLength(32)] public required string ProtocolVersion { get; init; }
    public uint? SteamAppId { get; init; }
    public Guid ClientRunId { get; init; }
    [Required, StringLength(GameAuthPolicy.TokenCharacters, MinimumLength = GameAuthPolicy.TokenCharacters)] public required string LinkChallenge { get; init; }
    [StringLength(GameAuthPolicy.TokenCharacters, MinimumLength = GameAuthPolicy.TokenCharacters)] public string? GameContextCredential { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class GameHandoffRequest
{
    [Required, StringLength(GameAuthPolicy.TokenCharacters, MinimumLength = GameAuthPolicy.TokenCharacters)] public required string AttemptCredential { get; init; }
    [Required, StringLength(GameAuthPolicy.TokenCharacters, MinimumLength = GameAuthPolicy.TokenCharacters)] public required string HandoffTicket { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SteamGameProofRequest
{
    [Required, StringLength(GameAuthPolicy.TokenCharacters, MinimumLength = GameAuthPolicy.TokenCharacters)] public required string AttemptCredential { get; init; }
    [Required, StringLength(GameAuthPolicy.MaximumSteamTicketHexCharacters, MinimumLength = 2)] public required string TicketHex { get; init; }
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
