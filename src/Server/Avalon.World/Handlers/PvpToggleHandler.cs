using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.World.Pvp;
using Avalon.World.Public;

namespace Avalon.World.Handlers;

/// <summary>CMSG_PVP_TOGGLE (#164). The same path as /pvp.</summary>
[PacketHandler(NetworkPacketType.CMSG_PVP_TOGGLE)]
public class PvpToggleHandler(PvpToggle toggle) : WorldPacketHandler<CPvpTogglePacket>
{
    public override void Execute(IWorldConnection connection, CPvpTogglePacket packet) => toggle.Toggle(connection);
}
