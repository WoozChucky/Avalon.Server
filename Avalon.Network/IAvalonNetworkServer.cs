namespace Avalon.Network;

public interface IAvalonNetworkServer : IDisposable
{
    public bool IsRunning { get; }
    public Task RunAsync(bool blocking = true);
    public Task StopAsync();
}
