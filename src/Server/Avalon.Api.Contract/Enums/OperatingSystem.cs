using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OperatingSystem : ushort
{
    Windows,
    MacOS,
    Linux
}
