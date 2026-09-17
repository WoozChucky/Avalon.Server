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
///
/// <see cref="Version"/> exists for the same reason: System.Text.Json binds a positional
/// record's parameters by name and silently fills in <see langword="default"/> for any
/// that are absent, so a blob written by a different build (a rolling deploy, or a schema
/// change) deserialises without throwing — it just produces a snapshot with the wrong
/// shape. A reader that trusts such a snapshot can null-reference. Instead, a reader
/// compares <see cref="Version"/> against <see cref="CurrentVersion"/> and treats a
/// mismatch as "absent", the same fail-closed behavior as an expired key. This is
/// deliberately not a migration framework — one int, one constant, one comparison.
/// </summary>
public sealed record WorldPresenceSnapshot(
    ushort WorldId,
    DateTime CapturedAt,
    IReadOnlyList<InstancePresenceSnapshot> Instances,
    int Version = WorldPresenceSnapshot.CurrentVersion)
{
    /// <summary>Current schema version stamped by the producer on every write.</summary>
    public const int CurrentVersion = 1;
}

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
