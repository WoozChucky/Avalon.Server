using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DamageType
{
    Physical = 0,
    Fire = 1,
    Cold = 2,
    Lightning = 3,
    Blight = 4,
}
