using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

/// <summary>Whether an aura helps or harms the unit it is on (auras): the stored numbers, so values are only ever appended.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuraKind
{
    Helpful = 1,
    Harmful = 2,
}
