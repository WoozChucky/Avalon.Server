using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorldType : ushort
{
    PvE,
    PvP,
    Event
}
