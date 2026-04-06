using Avalon.Network.Packets.Social;

namespace Avalon.World.Chat;

public interface ICommandDispatcher
{
    Task<bool> DispatchAsync(WorldPacketContext<CChatMessagePacket> ctx, CancellationToken token = default);
}
