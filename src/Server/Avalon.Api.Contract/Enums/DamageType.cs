using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DamageType
{
    Physical,
    Poison,
    Fire,
    Lightning,
}
