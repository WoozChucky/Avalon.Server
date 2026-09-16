using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

/// <summary>
/// HTTP-surface mirror of the world's MapType. Mirrored per the contract-enum
/// convention so Orval emits AvalonApiContractPresenceMapType and no
/// AvalonWorldPublicEnums* namespace leaks into the TypeScript client.
/// See docs/superpowers/specs/2026-04-22-contract-enum-mirroring-design.md.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PresenceMapType
{
    Unknown = 0,
    Town = 1,
    Normal = 2,
}
