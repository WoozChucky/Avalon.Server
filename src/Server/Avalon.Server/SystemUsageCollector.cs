using System.Diagnostics;
using System.Timers;
using Microsoft.Extensions.Logging;
using Timer = System.Timers.Timer;

namespace Avalon.Server;

public class SystemUsageCollector : IDisposable
{
    private readonly ILogger<SystemUsageCollector> _logger;
    private readonly Timer _timer;
    private readonly Process _process;
    
    private DateTime _lastTimeStamp;
    private TimeSpan _lastTotalProcessorTime = TimeSpan.Zero;
    private TimeSpan _lastUserProcessorTime = TimeSpan.Zero;
    private TimeSpan _lastPrivilegedProcessorTime = TimeSpan.Zero;
    
    public SystemUsageCollector(ILogger<SystemUsageCollector> logger)
    {
        _logger = logger;
        _process = Process.GetCurrentProcess();
        _timer = new Timer();
        _timer.Interval = 5000;
        _timer.AutoReset = true;
        _timer.Elapsed += CollectData;
    }
    
    public void Start()
    {
        _timer.Start();
    }
    
    public void Stop()
    {
        _timer.Stop();
    }
    
    public void Dispose()
    {
        _timer.Dispose();
    }

    private void CollectData(object? state, ElapsedEventArgs e)
    {
        double totalCpuTimeUsed = _process.TotalProcessorTime.TotalMilliseconds - _lastTotalProcessorTime.TotalMilliseconds;
        double privilegedCpuTimeUsed = _process.PrivilegedProcessorTime.TotalMilliseconds - _lastPrivilegedProcessorTime.TotalMilliseconds;
        double userCpuTimeUsed = _process.UserProcessorTime.TotalMilliseconds - _lastUserProcessorTime.TotalMilliseconds;

        _lastTotalProcessorTime = _process.TotalProcessorTime;
        _lastPrivilegedProcessorTime = _process.PrivilegedProcessorTime;
        _lastUserProcessorTime = _process.UserProcessorTime;

        // total CPU time available to the process, in milliseconds
        double cpuTimeElapsed = (DateTime.UtcNow - _lastTimeStamp).TotalMilliseconds * Environment.ProcessorCount;
        _lastTimeStamp = DateTime.UtcNow;
        
        _logger.LogTrace("Total CPU time: {0}", Math.Round(totalCpuTimeUsed * 100 / cpuTimeElapsed, 3));
        _logger.LogTrace("Privileged CPU time: {0}", Math.Round(privilegedCpuTimeUsed * 100 / cpuTimeElapsed, 3));
        _logger.LogTrace("User CPU time: {0}", Math.Round(userCpuTimeUsed * 100 / cpuTimeElapsed, 3));
        _logger.LogTrace("Working set: {0} MB", _process.WorkingSet64 / 1024 / 1024);
        _logger.LogTrace("Non-paged system memory: {0} MB", _process.NonpagedSystemMemorySize64 / 1024 / 1024);
        _logger.LogTrace("Paged memory: {0} MB", _process.PagedMemorySize64 / 1024 / 1024);
        _logger.LogTrace("Paged system memory: {0} MB", _process.PagedSystemMemorySize64 / 1024 / 1024);
        _logger.LogTrace("Private memory: {0} MB", _process.PrivateMemorySize64 / 1024 / 1024);
        _logger.LogTrace("Virtual memory: {0} MB", _process.VirtualMemorySize64 / 1024 / 1024);
        _logger.LogTrace("----------------------------------------");
    }
}
