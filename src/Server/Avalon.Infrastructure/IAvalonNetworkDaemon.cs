namespace Avalon.Infrastructure;

public interface IAvalonNetworkDaemon : IDisposable
{
    void Start();
    void Stop();
}
