using System.Reflection;
using Avalon.World.Public.Scripts;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Abstractions;

public interface IScriptHotReloader
{
    event ScriptsHotReloadedEventHandler? ScriptsHotReloaded;
    void Start();
    void Stop();

    void Update(out List<Type> scriptTypes);
}

/// <summary>Every AI script type of the new generation; the ones of the generation before are no longer in use.</summary>
public delegate void ScriptsHotReloadedEventHandler(List<Type> types);

/// <summary>
/// Turns each compile of the hot-reload scripts into a generation of AI scripts (#930). A compile that brings AI
/// scripts replaces the generation before: <see cref="ScriptsHotReloaded" /> and <see cref="Update" /> hand out its
/// types, and the replaced generation's context is unloaded, to be collected once nothing uses its types any more. A
/// compile that brings none replaces nothing, and its own context is unloaded at once.
/// </summary>
public class ScriptHotReloader : IScriptHotReloader
{
    public event ScriptsHotReloadedEventHandler? ScriptsHotReloaded;

    private readonly List<Type> _scriptTypes = [];
    private readonly object _generation = new();

    private readonly IScriptCompiler _scriptCompiler;
    private readonly ILogger<ScriptHotReloader> _logger;
    private ScriptAssemblyLoadContext? _current;
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

    private void OnScriptCompiled(CompiledScripts scripts)
    {
        List<Type> types = _active ? FindScriptTypes<AiScript>(scripts.Assembly) : [];

        if (types.Count == 0)
        {
            scripts.Context.Unload();
            return;
        }

        foreach (Type type in types)
        {
            WarnIfPinned(type);
        }

        // One generation at a time: two compiles finishing together replace each other in turn, and only the last
        // stays current.
        lock (_generation)
        {
            _logger.LogDebug("HotReloading {Count} AI scripts...", types.Count);
            ScriptsHotReloaded?.Invoke(types);
            lock (_scriptTypes)
            {
                _scriptTypes.Clear();
                _scriptTypes.AddRange(types);
            }

            // In use until the world has swapped the scripts it runs; unloading only lets it go once nothing does.
            _current?.Unload();
            _current = scripts.Context;
        }
    }

    public void Stop()
    {
        if (!_active) return;
        _active = false;
        _scriptCompiler.ScriptCompiled -= OnScriptCompiled;
        _scriptCompiler.Stop();
    }

    public void Update(out List<Type> scriptTypes)
    {
        lock (_scriptTypes)
        {
            if (_scriptTypes.Count == 0)
            {
                scriptTypes = [];
                return;
            }
            scriptTypes = [.. _scriptTypes];
            _scriptTypes.Clear();
        }
    }

    /// <summary>
    /// Warns, once for <paramref name="scriptType" />, when a public constructor of it takes a service closed over the
    /// script's own type (<c>ILogger&lt;TScript&gt;</c>, <c>IOptions&lt;Settings&lt;TScript&gt;&gt;</c>, ...): the
    /// container keeps what it resolved for the life of the process, and with it the type and its whole compile, so
    /// the generation is never collected. The script still loads and runs.
    /// </summary>
    private void WarnIfPinned(Type scriptType)
    {
        foreach (ConstructorInfo constructor in scriptType.GetConstructors())
        {
            foreach (ParameterInfo parameter in constructor.GetParameters())
            {
                if (!ClosesOver(parameter.ParameterType, scriptType))
                    continue;

                // Names only: a log record that held the types would hold the compile too, until it is written.
                _logger.LogWarning(
                    "Hot-reloaded script {Script} takes {ParameterType}, a service closed over its own type: the " +
                    "container keeps it for the life of the process, and with it this compile, which is never " +
                    "unloaded. Take ILoggerFactory (or a non-generic service) instead",
                    scriptType.FullName, parameter.ParameterType.ToString());
                return;
            }
        }
    }

    /// <summary>Whether <paramref name="type" />'s generic arguments, at any depth, include <paramref name="scriptType" />.</summary>
    private static bool ClosesOver(Type type, Type scriptType)
    {
        if (!type.IsConstructedGenericType)
            return false;

        foreach (Type argument in type.GenericTypeArguments)
        {
            if (argument == scriptType || ClosesOver(argument, scriptType))
                return true;
        }

        return false;
    }

    private List<Type> FindScriptTypes<TBaseType>(Assembly assembly)
    {
        Type baseType = typeof(TBaseType);
        var inheritedTypes = new List<Type>();

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

        return inheritedTypes;
    }
}
