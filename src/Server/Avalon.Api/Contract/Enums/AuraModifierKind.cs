using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

/// <summary>How an aura's stat modifier applies (auras): the stored numbers, so values are only ever appended.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuraModifierKind
{
    Flat = 1,
    Percent = 2,
}
