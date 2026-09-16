using System.Text.Json;
using System.Text.Json.Serialization;

namespace Avalon.Infrastructure.Presence;

/// <summary>
/// Wire contract for the live-presence snapshot. Written by Avalon.Server.World's
/// PresenceSnapshotService, read by Avalon.Api's ObservabilityService.
///
/// Deliberately plain primitives — no ValueObject&lt;T&gt;, no domain enums — so neither
/// side needs custom converters and the payload stays cheap to (de)serialise at 1 Hz.
/// Enums cross as strings because a snapshot outlives a deploy in Redis and numeric
/// enum drift between versions would silently mislabel data.
/// </summary>
public sealed record WorldPresenceSnapshot(
    ushort WorldId,
    DateTime CapturedAt,
    IReadOnlyList<InstancePresenceSnapshot> Instances);

public sealed record InstancePresenceSnapshot(
    Guid InstanceId,
    ushort TemplateId,
    int Seed,
    string MapType,
    string ConfigVersion,
    uint? OwnerCharacterId,
    IReadOnlyList<CharacterPresenceSnapshot> Characters);

public sealed record CharacterPresenceSnapshot(
    uint CharacterId,
    string Name,
    string Class,
    float X,
    float Y,
    float Z,
    float Orientation,
    ushort Level,
    uint CurrentHealth,
    uint Health,
    string MoveState,
    bool InCombat,
    bool Dead,
    DateTime LastSeen);

/// <summary>Points a character at the world whose snapshot holds them.</summary>
public sealed record CharacterPresenceIndex(ushort WorldId, Guid InstanceId);

public static class PresenceJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>
    /// Returns null rather than throwing on malformed input. A snapshot written by a
    /// different build is a recoverable "treat as absent", not a request failure.
    /// </summary>
    public static T? Deserialize<T>(string json) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json, Options); }
        catch (JsonException) { return null; }
    }
}
