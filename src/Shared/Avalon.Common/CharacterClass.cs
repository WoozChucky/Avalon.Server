using System.Text.Json.Serialization;

namespace Avalon.Common;

/// <summary>
/// A character's class. A wire enum: the packet contracts type their class members with it, so the exported schema
/// carries its names and numbers. Append-only.
/// </summary>
/// <remarks>
/// Lives in Avalon.Common because the domain, Avalon.World.Public, the REST contract and the packet contracts all
/// name it, and Avalon.Network.Packets references neither of the first two. The REST API writes it by name
/// (<see cref="JsonStringEnumConverter" />). A byte, stored in smallint columns (owner decision, #921, #922).
/// <see cref="None" /> is no class a character can have: it is what a message carries when no character sent it
/// (SChatMessagePacket), and the proto3 zero every enum needs. <see cref="CharacterClasses" /> holds the rest.
/// A number off the wire need not be a member: protobuf carries any value in an enum member, so a client's class
/// is checked where it is read (CharacterCreateHandler). One past 255 does not fit this type and fails the packet's
/// decode instead, which closes the connection.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CharacterClass : byte
{
    None = 0,
    Warrior = 1,
    Wizard = 2,
    Hunter = 3,
    Cleric = 4,
}
