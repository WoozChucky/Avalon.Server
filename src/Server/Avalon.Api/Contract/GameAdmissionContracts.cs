using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class GameJoinRequest
{
    [Required, StringLength(43, MinimumLength = 43)] public required string GameContextCredential { get; init; }
    [Range(1, ushort.MaxValue)] public ushort WorldId { get; init; }
    [Range(1, uint.MaxValue)] public uint? CharacterId { get; init; }
    public bool ConfirmTakeover { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class JoinRedemptionRequest
{
    [Required, StringLength(43, MinimumLength = 43)] public required string JoinTicket { get; init; }
    public Guid ConnectionId { get; init; }
    public Guid RedemptionId { get; init; }
}
