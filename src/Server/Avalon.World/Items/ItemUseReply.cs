using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Serialization;
using Avalon.World.Public;

namespace Avalon.World.Items;

/// <summary>Sends the one SMSG_ITEM_USE_RESULT a use is answered with, to the requester only.</summary>
public static class ItemUseReply
{
    public static void Send(IWorldConnection connection, uint requestId, ItemUseAnswer answer) =>
        connection.Send(SItemUseResultPacket.Create(requestId, answer.Result, answer.CooldownMs, answer.Message,
            PacketEncoder.Shared));
}
