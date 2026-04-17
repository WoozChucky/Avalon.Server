namespace Avalon.Hosting.Networking;

public interface IPacketHandlerNew
{
    Task ExecuteAsync(object context, CancellationToken token);
}
