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
    [Required, StringLength(43, MinimumLength = 43)] public required string GameContextCredential { get; init; }
    [Required, StringLength(128, MinimumLength = 43)] public required string PkceVerifier { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class GameLinkCompleteRequest
{
    [Required, StringLength(43, MinimumLength = 43)] public required string GameContextCredential { get; init; }
    [Required, StringLength(43, MinimumLength = 43)] public required string ConsentCode { get; init; }
    [Required, StringLength(128, MinimumLength = 43)] public required string PkceVerifier { get; init; }
    public bool Accepted { get; init; }
}
