using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

/// <summary>What an aura's ticks do (auras): the stored numbers, so values are only ever appended.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuraPeriodicKind
{
    None = 0,
    Damage = 1,
    Heal = 2,
}
