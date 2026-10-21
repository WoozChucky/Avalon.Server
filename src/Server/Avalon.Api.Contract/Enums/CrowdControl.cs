using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

/// <summary>
/// The states a harmful aura puts its holder in (auras), as flags so one aura can grant two; None for most auras. The
/// stored numbers, so values are only ever appended. A slow is not a flag: it is a harmful aura with a negative
/// movement speed modifier.
/// </summary>
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CrowdControl : byte
{
    None = 0,
    Stun = 1,
    Root = 2,
    Silence = 4,
}
