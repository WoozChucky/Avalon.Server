using Avalon.Hosting.Networking;

namespace Avalon.Server.World;

public interface IWorldPacketHandler<T> : IPacketHandlerNew
{
    Task ExecuteAsync(WorldPacketContext<T> ctx, CancellationToken token = default);
}
