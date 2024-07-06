using Microsoft.Extensions.Logging;
using MapInstance = Avalon.World.Maps.MapInstance;

namespace Avalon.World.Scripts;

public interface IAiController
{
    Task LoadAsync();

    Type? GetScriptTemplate(string name);

    Task Update(MapInstance instance, TimeSpan deltaTime);
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
        var aiScripts = typeof(AiScript)
            .Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(AiScript)) && !t.IsAbstract)
            .ToList();
        
        _logger.LogInformation("Loaded {Count} AI scripts", aiScripts.Count);
        
        _templateScripts = aiScripts.ToDictionary(t => t.Name, t => t);
        
        return Task.CompletedTask;
    }
    
    public Type? GetScriptTemplate(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (_templateScripts.TryGetValue(name, out var template)) return template;
        _logger.LogWarning("AI script {Name} not found", name);
        return null;
    }

    public async Task Update(MapInstance instance, TimeSpan deltaTime)
    {
        foreach (var (_, creature) in instance.Creatures)
        {
            creature.Script?.Update(deltaTime);
        }
    }
}
