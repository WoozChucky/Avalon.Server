using Avalon.Network.Packets.Social;

namespace Avalon.World.Chat;

public interface ICommand
{
    string Name { get; }
    string[] Aliases { get; }

    Task ExecuteAsync(WorldPacketContext<CChatMessagePacket> ctx, string[] args, CancellationToken token = default);
}
