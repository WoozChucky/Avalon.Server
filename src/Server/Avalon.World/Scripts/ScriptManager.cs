using System.Reflection;
using Avalon.World.Items;
using Avalon.World.Public.Scripts;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts;

public interface IScriptManager
{
    void Load();
    Type? GetAiScript(string name);
    Type? GetAbilityScript(string name);
    Type? GetQuestScript(string name);
    Type? GetItemScript(string name);

    /// <summary>The names an AI script can be given in a creature template, sorted. <c>[ChainedScript]</c> types are not among them.</summary>
    IReadOnlyList<string> AiScriptNames { get; }

    /// <summary>The names an ability script can be given in an ability template, sorted.</summary>
    IReadOnlyList<string> AbilityScriptNames { get; }

    /// <summary>The names a quest script can be given in a quest, sorted.</summary>
    IReadOnlyList<string> QuestScriptNames { get; }

    /// <summary>The names an item template's UseScript can give, sorted (item use).</summary>
    IReadOnlyList<string> ItemScriptNames { get; }

    /// <summary>
    /// Registers AI scripts compiled at runtime (the hot reloader's), by name, in place of any script of that name.
    /// <c>[ChainedScript]</c> types are skipped, as <see cref="Load"/> skips them.
    /// </summary>
    void RegisterHotReloaded(IEnumerable<Type> aiScriptTypes);
}

public class ScriptManager : IScriptManager
{
    private readonly ILogger<ScriptManager> _logger;

    // Replaced whole, never changed in place: the hot reloader's thread registers while the tick thread reads.
    private volatile IReadOnlyDictionary<string, Type> _aiScripts = new Dictionary<string, Type>();
    private volatile IReadOnlyDictionary<string, Type> _abilityScripts = new Dictionary<string, Type>();
    private volatile IReadOnlyDictionary<string, Type> _questScripts = new Dictionary<string, Type>();
    private volatile IReadOnlyDictionary<string, Type> _itemScripts = new Dictionary<string, Type>();
    private readonly object _registration = new();

    public ScriptManager(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ScriptManager>();
    }

    public void Load()
    {
        // A [ChainedScript] is a component another script builds and chains; it cannot be named in
        // a template's ScriptName, so it is not registered under a name at all.
        var aiScripts = FindScriptTypes<AiScript>()
            .Where(t => !t.IsDefined(typeof(ChainedScriptAttribute), inherit: false))
            .ToList();

        _logger.LogInformation("Loaded {Count} AI scripts", aiScripts.Count);

        _aiScripts = aiScripts.ToDictionary(t => t.Name, t => t);

        var abilityScripts = FindScriptTypes<AbilityScript>();

        _logger.LogInformation("Loaded {Count} ability scripts", abilityScripts.Count);

        _abilityScripts = abilityScripts.ToDictionary(t => t.Name, t => t);

        var questScripts = FindScriptTypes<QuestScript>();

        _logger.LogInformation("Loaded {Count} quest scripts", questScripts.Count);

        _questScripts = questScripts.ToDictionary(t => t.Name, t => t);

        var itemScripts = FindScriptTypes<ItemScript>();

        _logger.LogInformation("Loaded {Count} item scripts", itemScripts.Count);

        _itemScripts = itemScripts.ToDictionary(t => t.Name, t => t);
    }

    public IReadOnlyList<string> AiScriptNames => Sorted(_aiScripts);

    public IReadOnlyList<string> AbilityScriptNames => Sorted(_abilityScripts);

    public IReadOnlyList<string> QuestScriptNames => Sorted(_questScripts);

    public IReadOnlyList<string> ItemScriptNames => Sorted(_itemScripts);

    public void RegisterHotReloaded(IEnumerable<Type> aiScriptTypes)
    {
        lock (_registration)
        {
            Dictionary<string, Type> scripts = new(_aiScripts);
            foreach (Type type in aiScriptTypes.Where(t => !t.IsDefined(typeof(ChainedScriptAttribute), inherit: false)))
            {
                scripts[type.Name] = type;
            }

            _aiScripts = scripts;
        }
    }

    private static string[] Sorted(IReadOnlyDictionary<string, Type> scripts) =>
        scripts.Keys.Order(StringComparer.Ordinal).ToArray();

    public Type? GetAiScript(string name)
    {
        return _aiScripts.TryGetValue(name, out var scriptType) ? scriptType : null;
    }

    public Type? GetAbilityScript(string name)
    {
        return _abilityScripts.TryGetValue(name, out var scriptType) ? scriptType : null;
    }

    public Type? GetQuestScript(string name) =>
        _questScripts.TryGetValue(name, out Type? scriptType) ? scriptType : null;

    public Type? GetItemScript(string name) =>
        _itemScripts.TryGetValue(name, out Type? scriptType) ? scriptType : null;

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
