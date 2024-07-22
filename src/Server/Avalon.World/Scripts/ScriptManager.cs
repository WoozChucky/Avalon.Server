using System.Reflection;
using Avalon.World.Public.Scripts;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts;

public interface IScriptManager
{
    void Load();
    Type? GetAiScript(string name);
    Type? GetSpellScript(string name);
}

public class ScriptManager : IScriptManager
{
    private readonly ILogger<ScriptManager> _logger;
    
    private IDictionary<string, Type> _aiScripts;
    private IDictionary<string, Type> _spellScripts;
    
    public ScriptManager(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ScriptManager>();
    }
    
    public void Load()
    {
        var aiScripts = FindScriptTypes<AiScript>();
        
        _logger.LogInformation("Loaded {Count} AI scripts", aiScripts.Count);
        
        _aiScripts = aiScripts.ToDictionary(t => t.Name, t => t);
        
        var spellScripts = FindScriptTypes<SpellScript>();
        
        _logger.LogInformation("Loaded {Count} spell scripts", spellScripts.Count);
        
        _spellScripts = spellScripts.ToDictionary(t => t.Name, t => t);
    }

    public Type? GetAiScript(string name)
    {
        return _aiScripts.TryGetValue(name, out var scriptType) ? scriptType : null;
    }

    public Type? GetSpellScript(string name)
    {
        return _spellScripts.TryGetValue(name, out var scriptType) ? scriptType : null;
    }
    
    private List<Type> FindScriptTypes<TBaseType>()
    {
        var baseType = typeof(TBaseType);
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        
        var inheritedTypes = new List<Type>();
        
        foreach (var assembly in assemblies)
        {
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
            
        }
        
        return inheritedTypes;
    }
}
