using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Social;
using Avalon.World.Chat;
using Avalon.World.Public;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_CHAT_MESSAGE)]
public class ChatMessageHandler(IWorldServer worldServer, ICommandDispatcher commandDispatcher)
    : WorldPacketHandler<CChatMessagePacket>
{
    public override void Execute(IWorldConnection connection, CChatMessagePacket packet)
    {
        string message = packet.Message;

        if (message.StartsWith('/'))
        {
            WorldPacketContext<CChatMessagePacket> ctx = new() { Packet = packet, Connection = connection };

            connection.EnqueueContinuation(
                commandDispatcher.DispatchAsync(ctx),
                dispatched =>
                {
                    if (!dispatched)
                    {
                        connection.Send(SChatMessagePacket.Create(
                            0UL, 0UL, "System",
                            "Unknown command.",
                            packet.DateTime,
                            connection.CryptoSession.Encrypt));
                    }
                });

            return;
        }

        if (!connection.InGame)
        {
            return;
        }

        foreach (IWorldConnection target in worldServer.Connections)
        {
            if (!target.InGame || target.AccountId == connection.AccountId)
            {
                continue;
            }

            target.Send(SChatMessagePacket.Create(
                (ulong)(long)connection.AccountId!,
                (ulong)connection.Character!.Guid.Id,
                connection.Character.Name,
                message,
                packet.DateTime,
                target.CryptoSession.Encrypt));
        }
    }
}
