using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

/// <summary>What applying an aura again does (auras): the stored numbers, so values are only ever appended.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuraStacking
{
    Refresh = 1,
    Stack = 2,
    Independent = 3,
}
