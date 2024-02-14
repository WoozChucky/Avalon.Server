using Microsoft.Extensions.Logging;

namespace Avalon.Auth;

public class AvalonAuth : IAvalonAuth
{
    
    private readonly ILogger<AvalonAuth> _logger;
    private readonly CancellationTokenSource _cts;
    private volatile bool _isRunning;
    private long _loopCounter;
    
    public AvalonAuth(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<AvalonAuth>();
        _cts = new CancellationTokenSource();
    }
    
    public void Start()
    {
        _logger.LogInformation("Loading auth data");
        
        _isRunning = true;
        
        _logger.LogInformation("Starting auth loop");
    }

    public void Stop()
    {
        _logger.LogInformation("Stopping auth loop");
        _cts.Cancel();
        _isRunning = false;
    }

    public void Update(TimeSpan deltaTime)
    {
        
    }

    public bool IsRunning()
    {
        return _isRunning;
    }

    public void IncrementLoopCounter()
    {
        Interlocked.Increment(ref _loopCounter);
    }

    public long GetLoopCounter()
    {
        Interlocked.Read(ref _loopCounter);
        return _loopCounter;
    }
}
