using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class GameSessionControlRequest
{
    [Required, StringLength(19, MinimumLength = 1)] public required string AccountId { get; init; }
    public Guid GameSessionId { get; init; }
    [Required, StringLength(19, MinimumLength = 1)] public required string FencingToken { get; init; }

    /// <summary>
    /// Why the session ended, on <c>sessions/end</c> only (#967): a <c>GameSessionEndReasons</c> value. Absent from a world
    /// before #967; ignored on activate and heartbeat.
    /// </summary>
    [StringLength(32)] public string? Reason { get; init; }
}
