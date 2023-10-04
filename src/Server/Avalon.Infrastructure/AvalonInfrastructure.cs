using System.Diagnostics;
using Avalon.Common.Utils;
using Avalon.Game;
using Avalon.Metrics;
using Microsoft.Extensions.Logging;
using Timer = Avalon.Common.Utils.Timer;

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
        ILoggerFactory loggerFactory,
        CancellationTokenSource cts,
        IAvalonNetworkDaemon networkDaemon,
        IAvalonGame gameServer,
        IMetricsManager metricsManager)
    {
        _logger = loggerFactory.CreateLogger<AvalonInfrastructure>();
        _cts = cts;
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

    public void Update(CancellationTokenSource cts)
    {
        // TODO: Move this to a config file
        const uint minUpdateDiff = 1; // 1ms
        const int maxCoreStuckTime = 60000; // 60s
        const int halfMaxCoreStuckTime = maxCoreStuckTime / 2;
        
        var previousTime = Timer.CurrentTimeMillis();

        var timer = new System.Timers.Timer(60000);
        timer.Elapsed += (sender, args) =>
        {
            var numberOfThreads = Process.GetCurrentProcess().Threads.Count;
            if (numberOfThreads > 1)
            {
                _logger.LogInformation("Game UpdateLoop running on {NumberOfThreads} threads", numberOfThreads);
            }
        };
        timer.Start();
        
        while (_gameServer.IsRunning())
        {
            _gameServer.IncrementLoopCounter();
            
            var currentTime = Timer.CurrentTimeMillis();
            var diff = Timer.GetDiff(previousTime, currentTime);
            
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
            
            _gameServer.Update(diff.ToTimeSpan());
            
            previousTime = currentTime;

            if (cts.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        _logger.LogInformation("Game looped {LoopCounter} times", _gameServer.GetLoopCounter());
        _logger.LogInformation("Disposing AvalonInfrastructure...");
        _cts.Dispose();
        _metricsManager.QueueEvent("AvalonInfrastructureStatus", "Offline");
        GC.SuppressFinalize(this);
    }
}
