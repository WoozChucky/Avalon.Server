using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ItemRarity
{
    Junk,
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary
}
