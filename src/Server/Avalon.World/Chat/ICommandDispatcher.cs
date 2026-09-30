using Avalon.Network.Packets.Social;
using Avalon.World.Public;

namespace Avalon.World.Chat;

public interface ICommandDispatcher
{
    /// <summary>Tick thread. True when a command was found and the caller may run it (whether or not it failed).</summary>
    bool Dispatch(IWorldConnection connection, CChatMessagePacket packet);
}
