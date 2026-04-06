using Avalon.Network.Packets.Social;
using Avalon.World;
using Avalon.World.Chat;

namespace Avalon.Server.World.Handlers;

public class ChatMessageHandler(IWorldServer worldServer, ICommandDispatcher commandDispatcher)
    : IWorldPacketHandler<CChatMessagePacket>
{
    public async Task ExecuteAsync(WorldPacketContext<CChatMessagePacket> ctx, CancellationToken token = default)
    {
        var message = ctx.Packet.Message;

        if (message.StartsWith('/'))
        {
            bool dispatched = await commandDispatcher.DispatchAsync(ctx, token);

            if (!dispatched)
            {
                ctx.Connection.Send(SChatMessagePacket.Create(
                    0UL, 0UL, "System",
                    "Unknown command.",
                    ctx.Packet.DateTime,
                    ctx.Connection.CryptoSession.Encrypt));
            }

            return;
        }

        if (!ctx.Connection.InGame)
        {
            return;
        }

        var sender = ctx.Connection;

        foreach (var connection in worldServer.Connections)
        {
            if (!connection.InGame || connection.AccountId == sender.AccountId)
            {
                continue;
            }

            connection.Send(SChatMessagePacket.Create(
                (ulong)(long)sender.AccountId!,
                (ulong)sender.Character!.Guid.Id,
                sender.Character.Name,
                message,
                ctx.Packet.DateTime,
                connection.CryptoSession.Encrypt));
        }
    }
}
