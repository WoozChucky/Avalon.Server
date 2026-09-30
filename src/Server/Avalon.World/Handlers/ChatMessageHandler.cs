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
            // On the tick, to completion (spec 2026-09-30 section 5); a command's own async work comes
            // back through CommandContext.Then.
            if (!commandDispatcher.Dispatch(connection, packet))
            {
                connection.Send(SChatMessagePacket.Create(0UL, 0UL, "System", "Unknown command.", packet.DateTime,
                    connection.CryptoSession.Encrypt));
            }

            return;
        }

        if (!connection.InGame)
        {
            return;
        }

        connection.Send(SChatMessagePacket.Create(
            (ulong)(long)connection.AccountId!,
            connection.Character!.Guid.Id,
            connection.Character.Name,
            message,
            packet.DateTime,
            connection.CryptoSession.Encrypt));

        // TODO: Message should be sent to players in same instance as current connection, not all players in the world
        //  Also, in the future, we need to have chat channels (local, global, party, trade, system)
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
