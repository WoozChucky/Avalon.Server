using Avalon.Hosting.Networking;

namespace Avalon.World;

public interface IWorldPacketHandler<T> : IPacketHandlerNew
{
    Task ExecuteAsync(WorldPacketContext<T> ctx, CancellationToken token = default);

    Task IPacketHandlerNew.ExecuteAsync(object context, CancellationToken token)
        => ExecuteAsync((WorldPacketContext<T>)context, token);
}
