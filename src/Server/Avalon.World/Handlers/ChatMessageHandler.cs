using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Social;
using Avalon.World.Chat;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Social;

namespace Avalon.World.Handlers;

/// <summary>
/// A slash command runs on the tick; anything else is said aloud, to everyone in the sender's instance,
/// the sender included (spec 2026-09-30 section 5), except a listener that ignores the sender (#723). Before either,
/// a message over <see cref="ChatMessageLimit.MaxBytes" /> is refused (#936). This is the only handler of the opcode
/// and the only caller of the command dispatcher, so the rule holds for every command as for plain chat. A said line
/// goes out stripped and trimmed (<see cref="ChatText" />, #944), and one that says nothing then is dropped. Every line
/// is stamped with the container's clock in UTC, never with the time the client put on its message (#944).
/// </summary>
[PacketHandler(NetworkPacketType.CMSG_CHAT_MESSAGE)]
public class ChatMessageHandler(IWorld world, ICommandDispatcher commandDispatcher,
    ChatRateLimiter rateLimiter, TimeProvider time)
    : WorldPacketHandler<CChatMessagePacket>
{
    public override void Execute(IWorldConnection connection, CChatMessagePacket packet)
    {
        // A packet with no message says nothing.
        if (packet.Message is not { } message)
        {
            return;
        }

        // The length rule (#936) comes before everything, the commands included: an over-long message reaches no
        // parser, no lookup and no listener, uses none of the rate limit, and only the sender is told.
        if (ChatMessageLimit.Exceeds(message))
        {
            connection.Send(SChatMessagePacket.Refusal(ChatMessageLimit.TooLong, Now(), PacketEncoder.Shared));
            return;
        }

        if (message.StartsWith('/'))
        {
            if (!commandDispatcher.Dispatch(connection, packet))
            {
                connection.Send(SChatMessagePacket.Refusal("Unknown command.", Now(),
                    PacketEncoder.Shared));
            }

            return;
        }

        if (connection.Character is not { } sender)
        {
            return;
        }

        // A message that says nothing once stripped and trimmed (#944) is dropped: nobody hears it, nobody is told, and
        // it uses none of the rate limit.
        if (ChatText.SaysNothing(message))
        {
            return;
        }

        // Before anything is looked up or sent: an over-limit message is not said, and only the sender is told.
        if (!rateLimiter.Check(sender.Guid.Id, out TimeSpan retryAfter))
        {
            connection.Send(SChatMessagePacket.Refusal(ChatRateLimiter.TooFast(retryAfter), Now(),
                PacketEncoder.Shared));
            return;
        }

        // What is said: the message without the characters a line may not carry (#944), the message itself when clean.
        string said = ChatText.Clean(message);
        DateTime now = Now();
        ulong accountId = connection.AccountId is { } account ? (ulong)account.Value : 0UL;

        if (world.InstanceRegistry.GetInstanceById(sender.InstanceId) is not MapInstance instance)
        {
            // Nowhere to say it: the sender still sees its own line.
            connection.Send(SChatMessagePacket.Create(accountId, sender.Guid.Id, sender.Name, said, now,
                PacketEncoder.Shared, characterClass: sender.Class));
            rateLimiter.Record(sender.Guid.Id);
            return;
        }

        rateLimiter.Record(sender.Guid.Id);
        // Encoded once, for the first who hears it, and shared by everyone who does (#875).
        OutboundPacket line = default;
        foreach (IWorldConnection target in instance.Connections)
        {
            // A listener ignoring the sender does not hear it (#723); nobody is told.
            if (Ignoring.Hides(target, sender.Guid.Id))
                continue;

            if (line.Payload is null)
            {
                line = SChatMessagePacket.Create(accountId, sender.Guid.Id, sender.Name, said, now,
                    PacketEncoder.Shared, characterClass: sender.Class);
            }

            target.Send(line.Share());
        }

        line.Release();
    }

    // A line's stamp (#944): the server's clock, in UTC.
    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
