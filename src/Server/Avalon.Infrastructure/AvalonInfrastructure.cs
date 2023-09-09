using Avalon.Game;
using Avalon.Metrics;
using Microsoft.Extensions.Logging;

namespace Avalon.Infrastructure;

public interface IAvalonInfrastructure : IDisposable
{
    void Start();
    void Stop();
    void Update(CancellationTokenSource cancellationTokenSource);
}

public class AvalonInfrastructure : IAvalonInfrastructure
{
    private readonly CancellationTokenSource _cts;
    private readonly ILogger<AvalonInfrastructure> _logger;
    private readonly IAvalonNetworkDaemon _networkDaemon;
    private readonly IAvalonGame _gameServer;
    private readonly IMetricsManager _metricsManager;

    public AvalonInfrastructure(
        CancellationTokenSource cts,
        ILogger<AvalonInfrastructure> logger,
        IAvalonNetworkDaemon networkDaemon,
        IAvalonGame gameServer,
        IMetricsManager metricsManager)
    {
        _cts = cts;
        _logger = logger;
        _networkDaemon = networkDaemon;
        _gameServer = gameServer;
        _metricsManager = metricsManager;
    }

    public void Start()
    {
        _gameServer.Start();
        _networkDaemon.Start();
        
        _metricsManager.QueueEvent("AvalonInfrastructureStatus", "Online");
    }

    public void Stop()
    {
        _gameServer.Stop();
        _networkDaemon.Stop();
        
        _metricsManager.Stop();
        _cts.Cancel();
    }
    
    long getMSTimeDiff(long oldMSTime, long newMSTime)
    {
        // getMSTime() have limited data range and this is case when it overflow in this tick
        if (oldMSTime > newMSTime)
        {
            throw new Exception("getMSTimeDiff: oldMSTime > newMSTime");
            return (0xFFFFFFFF - oldMSTime) + newMSTime;
        }
        else
        {
            return newMSTime - oldMSTime;
        }
    }

    public void Update(CancellationTokenSource cts)
    {
        const uint minUpdateDiff = 1; // 1ms
        const int maxCoreStuckTime = 60000; // 60s
        const int halfMaxCoreStuckTime = maxCoreStuckTime / 2;
        
        var previousTime = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
        
        while (_gameServer.IsRunning())
        {
            _gameServer.IncrementLoopCounter();
            
            var currentTime = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
            
            var diff = getMSTimeDiff(previousTime, currentTime);
            if (diff < minUpdateDiff)
            {
                var sleepTime = minUpdateDiff - diff;
                if (sleepTime >= halfMaxCoreStuckTime)
                {
                    _logger.LogWarning("Game UpdateLoop waiting for {SleepTime}ms with MaxCoreStruckTime set to {MaxCoreStuckTime}ms", sleepTime, maxCoreStuckTime);
                }
                Thread.Sleep((int)sleepTime);
                continue;
            }
            
            _gameServer.Update(TimeSpan.FromMilliseconds(diff));
            
            previousTime = currentTime;

            if (cts.IsCancellationRequested)
            {
                break;
            }
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
