using System.Threading;
using System.Threading.Tasks;

namespace Avalon.Hosting.Networking;

public interface IPacketHandlerNew
{
    
}

public interface IWorldPacketHandler<T> : IPacketHandlerNew
{
    Task ExecuteAsync(WorldPacketContext<T> ctx, CancellationToken token = default);
}

public interface IAuthPacketHandler<T> : IPacketHandlerNew
{
    Task ExecuteAsync(AuthPacketContext<T> ctx, CancellationToken token = default);
}
