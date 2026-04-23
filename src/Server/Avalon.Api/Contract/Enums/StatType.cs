using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StatType
{
    Stamina,
    Strength,
    Agility,
    Intellect,
    Armor,
    BlockPct,
    DodgePct,
    CritPct,
    AttackDamage,
    AbilityDamage,
    Health,
    Power,
    AttackSpeed,
    MovementSpeed,
}
