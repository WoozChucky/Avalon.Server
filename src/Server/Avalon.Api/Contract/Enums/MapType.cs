using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MapType
{
    Town = 0,
    Normal = 1,
}
