namespace Avalon.Network;

public interface IAvalonNetworkServer : IDisposable
{
    public bool IsRunning { get; }
    public Task RunAsync();
    public Task StopAsync();
}
