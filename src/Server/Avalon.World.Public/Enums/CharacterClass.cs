using System.Text.Json.Serialization;

namespace Avalon.World.Public.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CharacterClass : ushort
{
    Warrior = 1,
    Wizard = 2,
    Hunter = 3,
    Healer = 4,
}
