using System.Text.Json.Serialization;

namespace Avalon.Common;

/// <summary>
/// A character's gender. One enum for the domain, Avalon.World.Public, the REST contract and the packet contracts
/// (#922); the REST API writes it by name (<see cref="JsonStringEnumConverter" />). There is no "unspecified" value:
/// a client that leaves it out of a character create sends 0, which is Male (owner decision, #922).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CharacterGender : byte
{
    Male = 0,
    Female = 1,
}
