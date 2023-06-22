using Avalon.Metrics;
using Microsoft.Extensions.Logging;

namespace Avalon.Infrastructure;

public interface IAvalonInfrastructure : IDisposable
{
    void Start();
    void Stop();
    void Loop(int waitTimeMs);
}

public class AvalonInfrastructure : IAvalonInfrastructure
{
    private readonly CancellationTokenSource _cts;
    private readonly ILogger<AvalonInfrastructure> _logger;
    private readonly IAvalonNetworkDaemon _networkDaemon;
    private readonly IMetricsManager _metricsManager;

    public AvalonInfrastructure(
        CancellationTokenSource cts,
        ILogger<AvalonInfrastructure> logger,
        IAvalonNetworkDaemon networkDaemon,
        IMetricsManager metricsManager)
    {
        _cts = cts;
        _logger = logger;
        _networkDaemon = networkDaemon;
        
        _metricsManager = metricsManager;
    }

    public void Start()
    {
        _networkDaemon.Start();
        
        _metricsManager.QueueEvent("AvalonInfrastructureStatus", "Online");
    }

    public void Stop()
    {
        _networkDaemon.Stop();
        
        _metricsManager.Stop();
        _cts.Cancel();
    }

    public void Loop(int waitTimeMs)
    {
        try
        {

            Thread.Sleep(waitTimeMs);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "");
            throw;
        }
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing AvalonInfrastructure...");
        _cts.Dispose();
        _metricsManager.QueueEvent("AvalonInfrastructureStatus", "Offline");
        GC.SuppressFinalize(this);
    }
}
