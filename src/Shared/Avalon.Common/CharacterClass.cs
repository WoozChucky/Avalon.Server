using System.Text.Json.Serialization;

namespace Avalon.Common;

/// <summary>
/// A character's class. Lives in Avalon.Common because the domain, Avalon.World.Public, the REST contract and the
/// packet contracts all name it, and Avalon.Network.Packets references neither of the first two. The REST API
/// writes it by name (<see cref="JsonStringEnumConverter" />).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CharacterClass : ushort
{
    Warrior = 1,
    Wizard = 2,
    Hunter = 3,
    Healer = 4,
}
