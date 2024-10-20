using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Abstractions;

public class ScriptMonitor
{
    private readonly ILogger<ScriptMonitor> _logger;
    private readonly FileSystemWatcher _watcher;

    private const string ScriptsPath = "Scripts";
    private const string ScriptExtension = ".dll";

    public ScriptMonitor(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ScriptMonitor>();

        var path = Path.Combine(Directory.GetCurrentDirectory(), ScriptsPath);

        _watcher = new FileSystemWatcher(path, $"*{ScriptExtension}")
        {
            EnableRaisingEvents = true,
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime
        };

        _watcher.Changed += OnScriptChanged;
        _watcher.Created += OnScriptChanged;
        _watcher.Deleted += OnScriptChanged;
        _watcher.Renamed += OnScriptChanged;
    }

    private void OnScriptChanged(object sender, FileSystemEventArgs e)
    {
        switch (e.ChangeType)
        {
            case WatcherChangeTypes.Changed:
                _logger.LogInformation("Script {Path} has been changed", e.FullPath);
                break;
            case WatcherChangeTypes.Created:
                _logger.LogInformation("Script {Path} has been created", e.FullPath);
                break;
            case WatcherChangeTypes.Deleted:
                _logger.LogInformation("Script {Path} has been deleted", e.FullPath);
                break;
            case WatcherChangeTypes.Renamed:
                _logger.LogInformation("Script {Path} has been renamed", e.FullPath);
                break;
            default:
                _logger.LogWarning("Unknown change type {ChangeType} for script {Path}", e.ChangeType, e.FullPath);
                break;
        }
    }

    public void Start()
    {
        _watcher.EnableRaisingEvents = true;
    }

    public void Stop()
    {
        _watcher.EnableRaisingEvents = false;
    }
}
