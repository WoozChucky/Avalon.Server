using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Social;
using Avalon.World.Chat;
using Avalon.World.Instances;
using Avalon.World.Public;

namespace Avalon.World.Handlers;

/// <summary>
/// A slash command runs on the tick; anything else is said aloud, to everyone in the sender's instance,
/// the sender included (spec 2026-09-30 section 5).
/// </summary>
[PacketHandler(NetworkPacketType.CMSG_CHAT_MESSAGE)]
public class ChatMessageHandler(IWorld world, ICommandDispatcher commandDispatcher,
    ChatRateLimiter rateLimiter)
    : WorldPacketHandler<CChatMessagePacket>
{
    public override void Execute(IWorldConnection connection, CChatMessagePacket packet)
    {
        string message = packet.Message;

        if (message.StartsWith('/'))
        {
            if (!commandDispatcher.Dispatch(connection, packet))
            {
                connection.Send(SChatMessagePacket.System("Unknown command.", packet.DateTime,
                    connection.CryptoSession.Encrypt));
            }

            return;
        }

        if (connection.Character is not { } sender)
        {
            return;
        }

        // Before anything is looked up or sent: an over-limit message is not said, and only the sender is told.
        if (!rateLimiter.Check(sender.Guid.Id, out TimeSpan retryAfter))
        {
            connection.Send(SChatMessagePacket.System(ChatRateLimiter.TooFast(retryAfter), packet.DateTime,
                connection.CryptoSession.Encrypt));
            return;
        }

        ulong accountId = connection.AccountId is { } account ? (ulong)account.Value : 0UL;

        if (world.InstanceRegistry.GetInstanceById(sender.InstanceId) is not MapInstance instance)
        {
            // Nowhere to say it: the sender still sees its own line.
            connection.Send(SChatMessagePacket.Create(accountId, sender.Guid.Id, sender.Name, message, packet.DateTime,
                connection.CryptoSession.Encrypt));
            rateLimiter.Record(sender.Guid.Id);
            return;
        }

        rateLimiter.Record(sender.Guid.Id);
        foreach (IWorldConnection target in instance.Connections)
        {
            target.Send(SChatMessagePacket.Create(accountId, sender.Guid.Id, sender.Name, message, packet.DateTime,
                target.CryptoSession.Encrypt));
        }
    }
}
