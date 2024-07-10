using System.Reflection;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts;

public interface IAiController
{
    Task LoadAsync();

    Type? GetScriptTemplate(string name);
}

public class AiController : IAiController
{
    private readonly ILogger<AiController> _logger;

    private Dictionary<string, Type> _templateScripts;

    public AiController(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<AiController>();
        _templateScripts = new Dictionary<string, Type>();
    }
    
    public Task LoadAsync()
    {
        var aiScripts = FindScriptTypes<AiScript>();
        
        _logger.LogInformation("Loaded {Count} AI scripts", aiScripts.Count);
        
        _templateScripts = aiScripts.ToDictionary(t => t.Name, t => t);
        
        return Task.CompletedTask;
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
    
    public Type? GetScriptTemplate(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (_templateScripts.TryGetValue(name, out var template)) return template;
        _logger.LogWarning("AI script {Name} not found", name);
        return null;
    }
}
