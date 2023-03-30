using Avalon.Network;
using Avalon.Network.Packets;

namespace Avalon.Game.Handlers;

public static class AvalonMovementManager
{
    public static Task HandleJumpPacket(TcpClient client, NetworkPacket packet)
    {
        return Task.CompletedTask;
    }

    public static Task HandleMovePacket(TcpClient client, NetworkPacket packet)
    {
        return Task.CompletedTask;
    }
}
