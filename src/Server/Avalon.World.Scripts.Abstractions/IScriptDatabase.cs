using Avalon.World.Public;
using Avalon.World.Public.Scripts;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Abstractions;

public interface IScriptDatabase
{
    IReadOnlyList<AiScript> Scripts { get; }
    
    void AddScript(AiScript script);
    void RemoveScript(AiScript script);
}

public class ScriptDatabase : IScriptDatabase
{
    private readonly object _lock = new();
    private readonly ILogger<ScriptDatabase> _logger;
    private readonly List<AiScript> _scripts;

    public ScriptDatabase(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ScriptDatabase>();
        _scripts = new List<AiScript>();
    }

    public IReadOnlyList<AiScript> Scripts
    {
        get
        {
            lock (_lock)
            {
                return _scripts.ToList().AsReadOnly();
            }
        }
    }
    
    public void AddScript(AiScript script)
    {
        lock (_lock)
        {
            if (_scripts.Contains(script))
            {
                _logger.LogWarning("Script {Script} already exists in the database, replacing it", script.GetType().Name);
                _scripts.Remove(script);
            }
            _scripts.Add(script);
        }
    }
    
    public void RemoveScript(AiScript script)
    {
        lock (_lock)
        {
            _scripts.Remove(script);
        }
    }
}
