using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.World.Public;

namespace Avalon.World.Handlers;

/// <summary>
/// CMSG_LEAVE_WORLD (#967): the player quit on purpose. The read loop noted the session's end as a logout as the packet
/// arrived (<c>WorldConnection.OnReceive</c>); here the connection the client left open is closed, which saves and
/// despawns the character as any close does, and identity, told <c>logout</c>, releases the account's place at once.
/// Never sent for Change Character (<c>CMSG_CHARACTER_LEAVE</c>), which keeps the connection and the session.
/// </summary>
[PacketHandler(NetworkPacketType.CMSG_LEAVE_WORLD)]
public class LeaveWorldHandler : WorldPacketHandler<CLeaveWorldPacket>
{
    public override void Execute(IWorldConnection connection, CLeaveWorldPacket packet)
    {
        if (connection.IsClosing) return;
        (connection as WorldConnection)?.NoteGoodbye();
        connection.Close();
    }
}
