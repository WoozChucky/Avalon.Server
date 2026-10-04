using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class GameAttemptRequest
{
    [Required, StringLength(16)] public required string ChannelHint { get; init; }
    [Required, StringLength(32)] public required string ProtocolVersion { get; init; }
    public Guid ClientRunId { get; init; }
    [Required, StringLength(43, MinimumLength = 43)] public required string LinkChallenge { get; init; }
    [StringLength(43, MinimumLength = 43)] public string? GameContextCredential { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class GameHandoffRequest
{
    [Required, StringLength(43, MinimumLength = 43)] public required string AttemptCredential { get; init; }
    [Required, StringLength(43, MinimumLength = 43)] public required string HandoffTicket { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SteamGameProofRequest
{
    [Required, StringLength(43, MinimumLength = 43)] public required string AttemptCredential { get; init; }
    [Required, StringLength(5120, MinimumLength = 2)] public required string TicketHex { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class GameContextRefreshRequest
{
    [Required, StringLength(43, MinimumLength = 43)] public required string GameContextRefreshToken { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class GameContextCredentialRequest
{
    [Required, StringLength(43, MinimumLength = 43)] public required string GameContextCredential { get; init; }
}
