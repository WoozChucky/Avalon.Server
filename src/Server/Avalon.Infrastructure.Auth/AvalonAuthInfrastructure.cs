using Avalon.Auth;
using Avalon.Common.Utils;
using Avalon.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Timer = Avalon.Common.Utils.Timer;

namespace Avalon.Infrastructure.Auth;

public class AvalonAuthInfrastructure : IAvalonInfrastructure
{
    private readonly CancellationTokenSource _cts;
    private readonly InfrastructureConfiguration _infrastructureConfiguration;
    private readonly ILogger<AvalonAuthInfrastructure> _logger;
    private readonly IAvalonNetworkDaemon _networkDaemon;
    private readonly IReplicatedCache _cache;
    private readonly IAvalonAuth _authServer;

    public AvalonAuthInfrastructure(
        ILoggerFactory loggerFactory,
        CancellationTokenSource cts,
        InfrastructureConfiguration infrastructureConfiguration,
        IAvalonNetworkDaemon networkDaemon,
        IReplicatedCache cache,
        IAvalonAuth authServer)
    {
        _logger = loggerFactory.CreateLogger<AvalonAuthInfrastructure>();
        _cts = cts;
        _infrastructureConfiguration = infrastructureConfiguration;
        _networkDaemon = networkDaemon;
        _cache = cache;
        _authServer = authServer;
    }

    public void Start()
    {
        _authServer.Start();
        _cache.ConnectAsync().Wait(); // TODO: async
        _networkDaemon.Start();
    }

    public void Stop()
    {
        _authServer.Stop();
        _networkDaemon.Stop();
        _cts.Cancel();
    }

    public void Update(CancellationTokenSource cts)
    {
        var halfMaxCoreStuckTime = _infrastructureConfiguration.MaxCoreStuckTime / 2;
        
        var previousTime = Timer.CurrentTimeMillis();
        
        while (_authServer.IsRunning())
        {
            _authServer.IncrementLoopCounter();
            
            var currentTime = Timer.CurrentTimeMillis();
            var diff = Timer.GetDiff(previousTime, currentTime);
            
            if (diff < _infrastructureConfiguration.MinUpdateDiff)
            {
                var sleepTime = _infrastructureConfiguration.MinUpdateDiff - diff;
                if (sleepTime >= halfMaxCoreStuckTime)
                {
                    _logger.LogWarning(
                        "Auth UpdateLoop waiting for {SleepTime}ms with MaxCoreStruckTime set to {MaxCoreStuckTime}ms",
                        sleepTime, 
                        _infrastructureConfiguration.MaxCoreStuckTime
                    );
                }
                Thread.Sleep((int)sleepTime);
                continue;
            }
            
            _authServer.Update(diff.ToTimeSpan());
            
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
        GC.SuppressFinalize(this);
    }
}
