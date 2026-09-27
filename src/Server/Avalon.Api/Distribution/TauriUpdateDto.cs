using System.Text.Json.Serialization;

namespace Avalon.Api.Distribution;

/// <summary>Tauri's updater response.</summary>
public sealed record TauriUpdateDto(
    string Version,
    string Notes,
    [property: JsonPropertyName("pub_date")] DateTimeOffset PubDate,
    IReadOnlyDictionary<string, TauriPlatformDto> Platforms);
