namespace Avalon.Common;

/// <summary>
/// Which of a character's containers a slot is in. A wire enum: the packet contracts type their container members
/// with it, so the exported schema carries its names and numbers. Append-only.
/// </summary>
/// <remarks>
/// Lives in Avalon.Common because the packet contracts, the domain and Avalon.World.Public all name it, and
/// Avalon.Network.Packets references neither of the other two. A CharacterInventory row stores the same number,
/// in a smallint column. A byte, because there will never be that many kinds of container (#913).
/// A number off the wire need not be a member: protobuf carries any value in an enum member, so a request's
/// container is checked where it is read (SlotRef.TryParse). One past 255 does not fit this type and fails the
/// packet's decode instead, which closes the connection.
/// </remarks>
public enum InventoryType : byte
{
    Equipment = 0,
    Bag = 1,
    Bank = 2,
}
