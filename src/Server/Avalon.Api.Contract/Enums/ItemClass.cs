using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ItemClass
{
    Consumable,
    Weapon,
    Armor,
    Quest,
    Crafting,
    Junk
}
