using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ItemRarity
{
    Common = 0,
    Magic = 1,
    Rare = 2,
    Unique = 3,
}
