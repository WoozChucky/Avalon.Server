using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorldStatus : ushort
{
    Offline,
    Online,
    Maintenance
}
