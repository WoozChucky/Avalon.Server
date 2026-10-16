using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

/// <summary>
/// <c>PUT /world/{id}/player-limit</c> (#967): the accounts the world admits, 1 to 100,000, or null for no limit. The
/// member is required, null included: an empty body is refused rather than read as "no limit".
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class WorldPlayerLimitRequest
{
    [JsonRequired, Range(1, 100_000)] public int? PlayerLimit { get; init; }
}
