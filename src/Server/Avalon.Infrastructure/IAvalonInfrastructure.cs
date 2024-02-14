namespace Avalon.Infrastructure;

public interface IAvalonInfrastructure : IDisposable
{
    void Start();
    void Stop();
    void Update(CancellationTokenSource cancellationTokenSource);
}
