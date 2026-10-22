using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

/// <summary>A stat an item gives: the stored numbers, so values are only ever appended.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StatType
{
    Stamina = 0,
    Strength = 1,
    Agility = 2,
    Intellect = 3,
    Armor = 4,
    BlockPct = 5,

    /// <summary>Evasion rating (#1178), formerly the dodge chance.</summary>
    Evasion = 6,
    CritPct = 7,
    AttackDamage = 8,
    AbilityDamage = 9,
    Health = 10,
    Power = 11,
    AttackSpeed = 12,
    MovementSpeed = 13,
    Ward = 14,
    FireResistance = 15,
    ColdResistance = 16,
    LightningResistance = 17,
    BlightResistance = 18,
}
