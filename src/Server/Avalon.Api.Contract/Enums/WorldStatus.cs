using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorldStatus : ushort
{
    Offline,
    Online,
    Maintenance,

    /// <summary>Online, with a player limit that places taken have reached (#967). Derived where places taken are known.</summary>
    Full
}
