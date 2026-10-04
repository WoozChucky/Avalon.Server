using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class GameSessionControlRequest
{
    [Required, StringLength(19, MinimumLength = 1)] public required string AccountId { get; init; }
    public Guid GameSessionId { get; init; }
    [Required, StringLength(19, MinimumLength = 1)] public required string FencingToken { get; init; }
}
