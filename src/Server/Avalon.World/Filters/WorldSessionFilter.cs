using Avalon.Network.Packets.Abstractions;
using Avalon.World.Public;

namespace Avalon.World.Filters;

public class WorldSessionFilter(IWorldConnection connection) : PacketFilter
{
    public override bool Process(NetworkPacket packet) => throw new NotImplementedException();

    public override bool CanProcess(NetworkPacketType type)
    {
        if (!connection.IsGameplayAuthorized) return false;
        // Pong is always valid regardless of character state
        if (type == NetworkPacketType.CMSG_PONG)
        {
            return true;
        }

        // The load report releases the readiness barrier, so it arrives while the character is
        // still pending and Character is null. Accepted whatever the character state, not only
        // while it is null: the barrier can expire and spawn between the packet being queued and
        // the pass that dispatches it, and a packet neither filter will take stays at the head of
        // the queue and blocks every packet behind it.
        if (type == NetworkPacketType.CMSG_CHARACTER_LOADED)
        {
            return true;
        }

        // A leave (#663) is answered in every state, refusals included, so it is accepted whatever the
        // connection holds. Here and never in the map filter: the session pass runs before any instance
        // ticks, so the despawn a leave starts never lands while an instance is walking its characters,
        // and a leave queued behind in-map packets waits at the head until the map pass has taken them.
        if (type == NetworkPacketType.CMSG_CHARACTER_LEAVE)
        {
            return true;
        }

        if (connection.Character != null)
        {
            return false;
        }

        return IsSelectPacket(type);
    }

    /// <summary>
    /// Whether this filter accepts the packet in some connection state, whatever the current one:
    /// <c>WorldConnection.OnReceive</c> tells a packet refused for the state it arrived in
    /// from one no filter takes at all.
    /// </summary>
    public static bool IsSessionPacket(NetworkPacketType type) =>
        type is NetworkPacketType.CMSG_PONG or NetworkPacketType.CMSG_CHARACTER_LOADED
            or NetworkPacketType.CMSG_CHARACTER_LEAVE || IsSelectPacket(type);

    /// <summary>The select-phase packets, accepted only while the connection holds no character.</summary>
    private static bool IsSelectPacket(NetworkPacketType type) =>
        type is NetworkPacketType.CMSG_CHARACTER_LIST or NetworkPacketType.CMSG_CHARACTER_CREATE
            or NetworkPacketType.CMSG_CHARACTER_DELETE or NetworkPacketType.CMSG_CHARACTER_SELECTED;
}
