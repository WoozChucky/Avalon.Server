using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

/// <summary>A stat an aura modifies (auras): the stored numbers, so values are only ever appended.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuraStat
{
    Armor = 1,
    AttackDamage = 2,
    AbilityDamage = 3,
    CritPct = 4,
    DodgePct = 5,
    BlockPct = 6,
    HastePct = 7,
    MovementSpeed = 8,
    MaxHealth = 9,
    MaxPower = 10,
}
