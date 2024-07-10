using System.Reflection;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Abstractions;

public interface IScriptHotReloader
{
    event ScriptsHotReloadedEventHandler? ScriptsHotReloaded;
    void Start();
    void Stop();
}

public delegate void ScriptsHotReloadedEventHandler(List<Type> types);

public class ScriptHotReloader : IScriptHotReloader
{
    public event ScriptsHotReloadedEventHandler? ScriptsHotReloaded;
    
    private readonly IScriptCompiler _scriptCompiler;
    private readonly ILogger<ScriptHotReloader> _logger;
    private bool _active;
    
    public ScriptHotReloader(ILoggerFactory loggerFactory, IScriptCompiler scriptCompiler)
    {
        _scriptCompiler = scriptCompiler;
        _logger = loggerFactory.CreateLogger<ScriptHotReloader>();
        _active = false;
    }

    public void Start()
    {
        if (_active) return;
        _active = true;
        _scriptCompiler.ScriptCompiled += OnScriptCompiled;
        _scriptCompiler.Start();
    }

    private void OnScriptCompiled(Assembly assembly)
    {
        if (!_active) return;

        var types = FindScriptTypes<AiScript>(assembly);

        if (types.Count == 0) return;
        
        _logger.LogDebug("HotReloading {Count} AI scripts...", types.Count);
        ScriptsHotReloaded?.Invoke(types);
    }

    public void Stop()
    {
        if (!_active) return;
        _active = false;
        _scriptCompiler.ScriptCompiled -= OnScriptCompiled;
        _scriptCompiler.Stop();
    }
    
    private List<Type> FindScriptTypes<TBaseType>(Assembly assembly)
    {
        var baseType = typeof(TBaseType);
        var inheritedTypes = new List<Type>();
        
        try
        {
            var types = assembly.GetTypes();
            foreach (var type in types)
            {
                if (type.IsSubclassOf(baseType) && !type.IsAbstract)
                {
                    inheritedTypes.Add(type);
                }
            }
        }
        catch (ReflectionTypeLoadException e)
        {
            _logger.LogError(e, "Failed to load types from assembly {Assembly}", assembly.FullName);
            foreach (var loaderException in e.LoaderExceptions)
            {
                _logger.LogError(loaderException, "Loader exception");
            }
        }
        
        return inheritedTypes;
    }
}
