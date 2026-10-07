using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InventoryType : ushort
{
    Equipment,
    Bag,
    Bank,
}
