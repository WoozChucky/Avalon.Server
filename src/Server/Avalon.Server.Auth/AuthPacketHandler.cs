using Avalon.Hosting.Networking;

namespace Avalon.Server.Auth;

public interface IAuthPacketHandler<T> : IPacketHandlerNew
{
    Task ExecuteAsync(AuthPacketContext<T> ctx, CancellationToken token = default);

    Task IPacketHandlerNew.ExecuteAsync(object context, CancellationToken token)
        => ExecuteAsync((AuthPacketContext<T>)context, token);
}
