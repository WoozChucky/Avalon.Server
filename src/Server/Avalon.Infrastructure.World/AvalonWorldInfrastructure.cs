using System.Diagnostics;
using Avalon.Common.Utils;
using Avalon.Game;
using Avalon.Infrastructure.Configuration;
using Avalon.Metrics;
using Microsoft.Extensions.Logging;
using Timer = Avalon.Common.Utils.Timer;

namespace Avalon.Infrastructure.World;

public class AvalonWorldInfrastructure : IAvalonInfrastructure
{
    private readonly CancellationTokenSource _cts;
    private readonly InfrastructureConfiguration _infrastructureConfiguration;
    private readonly ILogger<AvalonWorldInfrastructure> _logger;
    private readonly IAvalonNetworkDaemon _networkDaemon;
    private readonly IAvalonGame _gameServer;
    private readonly IReplicatedCache _cache;
    private readonly IMetricsManager _metricsManager;

    public AvalonWorldInfrastructure(
        ILoggerFactory loggerFactory,
        CancellationTokenSource cts,
        InfrastructureConfiguration infrastructureConfiguration,
        IAvalonNetworkDaemon networkDaemon,
        IAvalonGame gameServer,
        IReplicatedCache cache,
        IMetricsManager metricsManager)
    {
        _logger = loggerFactory.CreateLogger<AvalonWorldInfrastructure>();
        _cts = cts;
        _infrastructureConfiguration = infrastructureConfiguration;
        _networkDaemon = networkDaemon;
        _gameServer = gameServer;
        _cache = cache;
        _metricsManager = metricsManager;
    }

    public void Start()
    {
        _gameServer.Start();
        _cache.ConnectAsync().Wait(); // TODO: async
        _networkDaemon.Start();
        
        _metricsManager.QueueEvent("AvalonInfrastructureStatus", "Online");
    }

    public void Stop()
    {
        _gameServer.Stop();
        _cache.DisconnectAsync().Wait(); // TODO: async
        _networkDaemon.Stop();
        
        _metricsManager.Stop();
        _cts.Cancel();
    }

    public void Update(CancellationTokenSource cts)
    {
        var halfMaxCoreStuckTime = _infrastructureConfiguration.MaxCoreStuckTime / 2;
        
        var previousTime = Timer.CurrentTimeMillis();

        var timer = new System.Timers.Timer(60000);
        timer.Elapsed += (sender, args) =>
        {
            var numberOfThreads = Process.GetCurrentProcess().Threads.Count;
            if (numberOfThreads > 1)
            {
                _logger.LogTrace("Game UpdateLoop running on {NumberOfThreads} threads", numberOfThreads);
            }
        };
        timer.Start();
        
        while (_gameServer.IsRunning())
        {
            _gameServer.IncrementLoopCounter();
            
            var currentTime = Timer.CurrentTimeMillis();
            var diff = Timer.GetDiff(previousTime, currentTime);
            
            if (diff < _infrastructureConfiguration.MinUpdateDiff)
            {
                var sleepTime = _infrastructureConfiguration.MinUpdateDiff - diff;
                if (sleepTime >= halfMaxCoreStuckTime)
                {
                    _logger.LogWarning(
                        "Game UpdateLoop waiting for {SleepTime}ms with MaxCoreStruckTime set to {MaxCoreStuckTime}ms",
                        sleepTime, 
                        _infrastructureConfiguration.MaxCoreStuckTime
                    );
                }
                Thread.Sleep((int)sleepTime);
                continue;
            }
            
            _gameServer.Update(diff.ToTimeSpan());
            
            previousTime = currentTime;
            
            _metricsManager.QueueMetric("server.loop.counter", _gameServer.GetLoopCounter());

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
