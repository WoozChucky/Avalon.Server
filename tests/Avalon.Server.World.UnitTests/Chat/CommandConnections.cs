using System.IO;
using Avalon.Common.Accounts;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Social;
using Avalon.World.Public;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.World.UnitTests.Chat;

/// <summary>A connection for command tests: records what it is sent and runs continuations inline.</summary>
internal sealed class CommandConnection
{
    public IWorldConnection Connection { get; } = Substitute.For<IWorldConnection>();
    public List<NetworkPacket> Sent { get; } = [];

    public CommandConnection(AccountAccessLevel level = AccountAccessLevel.Player)
    {
        Connection.AccessLevel.Returns(level);
        Connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        Connection.When(c => c.Send(Arg.Any<NetworkPacket>())).Do(ci => Sent.Add(ci.Arg<NetworkPacket>()));

        // What the tick does a tick later, done now: the task settles, then the callback runs.
        Connection.When(c => c.EnqueueContinuation(Arg.Any<Task>(), Arg.Any<Action>()))
            .Do(ci =>
            {
                ci.Arg<Task>().Wait(TimeSpan.FromSeconds(5));
                ci.Arg<Action>()();
            });
    }

    /// <summary>Every chat line this connection was sent, oldest first. The crypto session is a pass-through.</summary>
    public List<string> Messages() => Sent
        .Where(p => p.Header.Type == NetworkPacketType.SMSG_CHAT_MESSAGE)
        .Select(p =>
        {
            using var stream = new MemoryStream(p.Payload);
            return Serializer.Deserialize<SChatMessagePacket>(stream).Message;
        })
        .ToList();
}
