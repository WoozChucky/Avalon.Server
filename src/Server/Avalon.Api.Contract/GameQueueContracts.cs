using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Avalon.Common.GameAuth;

namespace Avalon.Api.Contract;

/// <summary>The body of <c>POST /game/queue/status</c> and <c>/game/queue/leave</c> (#967).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class GameQueueRequest
{
    [Required, StringLength(GameAuthPolicy.TokenCharacters, MinimumLength = GameAuthPolicy.TokenCharacters)] public required string GameContextCredential { get; init; }
    [Range(1, ushort.MaxValue)] public ushort WorldId { get; init; }
}

/// <summary>
/// A waiter's state (#967): <c>queued</c>, <c>paused</c> (the world is offline or in maintenance: positions kept, nobody
/// enters), <c>your_turn</c> (ask for a join ticket within the turn window) or <c>not_queued</c> (ask to join). Position
/// from 1; <see cref="CheckInAfterSeconds"/> when to ask again; <see cref="EstimatedWaitSeconds"/> is reserved, always
/// null. <see cref="Error"/> is set only on a refusal.
/// </summary>
public sealed class GameQueueStatusReply
{
    public string? State { get; init; }
    public int? Position { get; init; }
    public int? CheckInAfterSeconds { get; init; }
    public int? EstimatedWaitSeconds { get; init; }
    public string? Error { get; init; }
}
