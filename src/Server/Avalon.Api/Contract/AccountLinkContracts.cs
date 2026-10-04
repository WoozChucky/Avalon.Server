using Avalon.Common.GameAuth;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AccountLinkConfirmRequest
{
    public Guid PendingLinkId { get; init; }
    [Required, StringLength(256)] public required string CurrentPassword { get; init; }
    [StringLength(6, MinimumLength = 6)] public string? MfaCode { get; init; }
    public bool Confirmed { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class GameLinkProposalRequest
{
    [Required, StringLength(GameAuthPolicy.TokenCharacters, MinimumLength = GameAuthPolicy.TokenCharacters)] public required string GameContextCredential { get; init; }
    [Required, StringLength(GameAuthPolicy.MaximumPkceVerifierCharacters, MinimumLength = GameAuthPolicy.TokenCharacters)] public required string PkceVerifier { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class GameLinkCompleteRequest
{
    [Required, StringLength(GameAuthPolicy.TokenCharacters, MinimumLength = GameAuthPolicy.TokenCharacters)] public required string GameContextCredential { get; init; }
    [Required, StringLength(GameAuthPolicy.TokenCharacters, MinimumLength = GameAuthPolicy.TokenCharacters)] public required string ConsentCode { get; init; }
    [Required, StringLength(GameAuthPolicy.MaximumPkceVerifierCharacters, MinimumLength = GameAuthPolicy.TokenCharacters)] public required string PkceVerifier { get; init; }
    public bool Accepted { get; init; }
}
