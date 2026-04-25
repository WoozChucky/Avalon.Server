using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CharacterGender : byte
{
    Male = 0,
    Female = 1,
}
