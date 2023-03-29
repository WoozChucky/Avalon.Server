using System.Net.Sockets;

namespace Avalon.Network.Packets;

public interface IPacketHandler
{
    Task HandleAsync(TcpClient client, NetworkPacket packet);
}
