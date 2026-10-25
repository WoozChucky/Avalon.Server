using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

/// <summary>
/// A stat that belongs to a base (#1179): its defence or its weapon numbers, a range on a template and one rolled value
/// on each copy. Units: armour points, evasion rating, ward points, block chance in percentage points, damage in points,
/// attack time in milliseconds before attack speed.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ItemBaseStat : byte
{
    Armour = 0,
    Evasion = 1,
    Ward = 2,
    BlockChance = 3,
    DamageMin = 4,
    DamageMax = 5,
    AttackTime = 6,
}
