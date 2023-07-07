using Avalon.Game.Maps;
using Microsoft.Extensions.Logging;

namespace Avalon.Game.Scripts;

public interface IAIController
{
    void LoadScripts();

    Type? GetScriptTemplate(string name);

    Task Update(MapInstance instance, TimeSpan deltaTime);
}

public class AIController : IAIController
{
    private readonly ILogger<AIController> _logger;

    private Dictionary<string, Type> _templateScripts;

    public AIController(ILogger<AIController> logger)
    {
        _logger = logger;
    }
    
    public void LoadScripts()
    {
        var aiScripts = typeof(AIScript)
            .Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(AIScript)) && !t.IsAbstract);
        
        _logger.LogInformation("Loaded {Count} AI scripts", aiScripts.Count());
        
        _templateScripts = aiScripts.ToDictionary(t => t.Name, t => t);
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
