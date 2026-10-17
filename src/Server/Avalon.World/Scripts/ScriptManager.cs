using System.Reflection;
using Avalon.World.Abilities;
using Avalon.World.Auras;
using Avalon.World.Doodads;
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
    Type? GetAuraScript(string name);
    Type? GetDoodadScript(string name);

    /// <summary>The names an AI script can be given in a creature template, sorted. <c>[ChainedScript]</c> types are not among them.</summary>
    IReadOnlyList<string> AiScriptNames { get; }

    /// <summary>The names an ability script can be given in an ability template, sorted.</summary>
    IReadOnlyList<string> AbilityScriptNames { get; }

    /// <summary>The names a quest script can be given in a quest, sorted.</summary>
    IReadOnlyList<string> QuestScriptNames { get; }

    /// <summary>The names an item template's UseScript can give, sorted (item use).</summary>
    IReadOnlyList<string> ItemScriptNames { get; }

    /// <summary>The names an aura template's ScriptName can give, sorted.</summary>
    IReadOnlyList<string> AuraScriptNames { get; }

    /// <summary>The names a doodad template's ScriptName can give, sorted.</summary>
    IReadOnlyList<string> DoodadScriptNames { get; }

    /// <summary>
    /// Registers a generation of AI scripts compiled at runtime (the hot reloader's), by name, in place of any built-in
    /// script of that name and of the generation registered before, which is dropped whole (#930): a name only an
    /// older generation had is no longer registered. <c>[ChainedScript]</c> types are skipped, as <see cref="Load"/>
    /// skips them.
    /// </summary>
    void RegisterHotReloaded(IEnumerable<Type> aiScriptTypes);
}

/// <summary>
/// Finds every script by type name. <see cref="Load" /> scans the assemblies the process was built with (none loaded
/// at runtime: not a collectible one, not a dynamic one) and lays the hot reloader's current generation over them, so
/// the newest copy of a name wins and a type compiled or emitted at runtime by anything else is never named (#930).
/// </summary>
/// <remarks>
/// The manager holds the types of the built-in scripts and of one hot-reloaded generation only: once a generation is
/// replaced, nothing here keeps its load context in memory.
/// </remarks>
public class ScriptManager : IScriptManager
{
    private readonly ILogger<ScriptManager> _logger;

    // The built-in AI scripts, from the last Load, and the hot-reloaded generation laid over them. Under _registration.
    private IReadOnlyDictionary<string, Type> _builtInAiScripts = new Dictionary<string, Type>(StringComparer.Ordinal);
    private IReadOnlyList<Type> _hotReloadedAiScripts = [];

    // Replaced whole, never changed in place: the hot reloader's thread registers while the tick thread reads.
    private volatile IReadOnlyDictionary<string, Type> _aiScripts = new Dictionary<string, Type>(StringComparer.Ordinal);
    private volatile IReadOnlyDictionary<string, Type> _abilityScripts = new Dictionary<string, Type>(StringComparer.Ordinal);
    private volatile IReadOnlyDictionary<string, Type> _questScripts = new Dictionary<string, Type>(StringComparer.Ordinal);
    private volatile IReadOnlyDictionary<string, Type> _itemScripts = new Dictionary<string, Type>(StringComparer.Ordinal);
    private volatile IReadOnlyDictionary<string, Type> _auraScripts = new Dictionary<string, Type>(StringComparer.Ordinal);
    private volatile IReadOnlyDictionary<string, Type> _doodadScripts = new Dictionary<string, Type>(StringComparer.Ordinal);
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

        lock (_registration)
        {
            _builtInAiScripts = aiScripts.ToDictionary(t => t.Name, t => t, StringComparer.Ordinal);
            _aiScripts = WithHotReloaded(_builtInAiScripts, _hotReloadedAiScripts);
        }

        List<Type> abilityScripts = FindScriptTypes<AbilityScript>();

        _logger.LogInformation("Loaded {Count} ability scripts", abilityScripts.Count);

        _abilityScripts = abilityScripts.ToDictionary(t => t.Name, t => t, StringComparer.Ordinal);
        PrepareAbilityScripts(abilityScripts);

        List<Type> questScripts = FindScriptTypes<QuestScript>();

        _logger.LogInformation("Loaded {Count} quest scripts", questScripts.Count);

        _questScripts = questScripts.ToDictionary(t => t.Name, t => t, StringComparer.Ordinal);

        List<Type> itemScripts = FindScriptTypes<ItemScript>();

        _logger.LogInformation("Loaded {Count} item scripts", itemScripts.Count);

        _itemScripts = itemScripts.ToDictionary(t => t.Name, t => t, StringComparer.Ordinal);

        List<Type> auraScripts = FindScriptTypes<AuraScript>();

        _logger.LogInformation("Loaded {Count} aura scripts", auraScripts.Count);

        _auraScripts = auraScripts.ToDictionary(t => t.Name, t => t, StringComparer.Ordinal);

        List<Type> doodadScripts = FindScriptTypes<DoodadScript>();

        _logger.LogInformation("Loaded {Count} doodad scripts", doodadScripts.Count);

        _doodadScripts = doodadScripts.ToDictionary(t => t.Name, t => t, StringComparer.Ordinal);
    }

    public IReadOnlyList<string> AiScriptNames => Sorted(_aiScripts);

    public IReadOnlyList<string> AbilityScriptNames => Sorted(_abilityScripts);

    public IReadOnlyList<string> QuestScriptNames => Sorted(_questScripts);

    public IReadOnlyList<string> ItemScriptNames => Sorted(_itemScripts);

    public IReadOnlyList<string> AuraScriptNames => Sorted(_auraScripts);

    public IReadOnlyList<string> DoodadScriptNames => Sorted(_doodadScripts);

    public void RegisterHotReloaded(IEnumerable<Type> aiScriptTypes)
    {
        Type[] generation = aiScriptTypes.Where(t => !t.IsDefined(typeof(ChainedScriptAttribute), inherit: false)).ToArray();
        lock (_registration)
        {
            _hotReloadedAiScripts = generation;
            _aiScripts = WithHotReloaded(_builtInAiScripts, generation);
        }
    }

    // Built from the built-in scripts every time, never from the map before: that one may hold an older generation.
    private static Dictionary<string, Type> WithHotReloaded(IReadOnlyDictionary<string, Type> builtIn,
        IReadOnlyList<Type> generation)
    {
        Dictionary<string, Type> scripts = new(builtIn, StringComparer.Ordinal);
        foreach (Type type in generation)
        {
            scripts[type.Name] = type;
        }

        return scripts;
    }

    /// <summary>
    /// Has each ability script's factory made now, off the tick, rather than by its first cast (#925). A type the
    /// cast arguments cannot build is left for its cast to refuse and log: one built only by another script, with
    /// arguments of its own, is no fault.
    /// </summary>
    private void PrepareAbilityScripts(List<Type> abilityScripts)
    {
        foreach (Type type in abilityScripts)
        {
            try
            {
                AbilityScriptActivator.Prepare(type);
            }
            catch (Exception e)
            {
                _logger.LogDebug(e, "Ability script {ScriptType} cannot be built from a cast's arguments", type.FullName);
            }
        }
    }

    private static string[] Sorted(IReadOnlyDictionary<string, Type> scripts) =>
        scripts.Keys.Order(StringComparer.Ordinal).ToArray();

    public Type? GetAiScript(string name)
    {
        return _aiScripts.TryGetValue(name, out Type? scriptType) ? scriptType : null;
    }

    public Type? GetAbilityScript(string name)
    {
        return _abilityScripts.TryGetValue(name, out Type? scriptType) ? scriptType : null;
    }

    public Type? GetQuestScript(string name) =>
        _questScripts.TryGetValue(name, out Type? scriptType) ? scriptType : null;

    public Type? GetItemScript(string name) =>
        _itemScripts.TryGetValue(name, out Type? scriptType) ? scriptType : null;

    public Type? GetAuraScript(string name) =>
        _auraScripts.TryGetValue(name, out Type? scriptType) ? scriptType : null;

    public Type? GetDoodadScript(string name) =>
        _doodadScripts.TryGetValue(name, out Type? scriptType) ? scriptType : null;

    /// <summary>
    /// The concrete <typeparamref name="TBaseType" /> subclasses of the built-in assemblies: every one loaded but those
    /// made at runtime. A collectible one is a hot-reload generation, which reaches the manager through
    /// <see cref="RegisterHotReloaded" /> alone, or something else's, as is a dynamic one; scanned, an older or unrelated
    /// copy of a name could be picked over the current one, and would be held for good.
    /// </summary>
    private List<Type> FindScriptTypes<TBaseType>()
    {
        Type baseType = typeof(TBaseType);
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

        var inheritedTypes = new List<Type>();

        foreach (Assembly assembly in assemblies)
        {
            if (assembly.IsDynamic || assembly.IsCollectible)
            {
                continue;
            }

            try
            {
                Type[] types = assembly.GetTypes();
                foreach (Type type in types)
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
                foreach (Exception? loaderException in e.LoaderExceptions)
                {
                    _logger.LogError(loaderException, "Loader exception");
                }
            }

        }

        return inheritedTypes;
    }
}
