using System.Collections.Concurrent;
using Avalon.World.Quests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Doodads;

/// <summary>
/// Builds doodad scripts: each type once, through QuestScriptServices, so a script can obtain loggers and the clock and
/// nothing that writes, and shares it with every doodad that names it. A type that cannot be built is logged and its
/// doodads run without a script. Keeps the throttles their hooks' failures are logged through, at most once per script
/// and hook per ThrottledErrorLog.Interval. A DI singleton, asked on the tick and by the off-tick instance builds
/// (OnSpawn), so both its caches are safe across threads; the scripts it hands out keep no state.
/// </summary>
public sealed class DoodadScripts(IServiceProvider services, TimeProvider time, ILogger<DoodadScripts> logger)
{
    private readonly QuestScriptServices _narrowed = new(services);
    private readonly ConcurrentDictionary<Type, DoodadScript?> _built = new();
    private readonly Dictionary<(Type Script, string Hook), ThrottledErrorLog> _failures = [];
    private readonly Lock _failuresGate = new();

    /// <summary>
    /// The script the template names, built on its first ask and the same one on every other; null with none, or when
    /// its type cannot be built (logged once). Allocates nothing once built.
    /// </summary>
    public DoodadScript? For(DoodadTemplateView template) =>
        template.ScriptType is { } type ? _built.GetOrAdd(type, static (t, self) => self.Build(t), this) : null;

    /// <summary>
    /// Whether <paramref name="scriptType" />, a DoodadScript, declares its own <see cref="DoodadScript.CanSee" />: only
    /// then is it asked who sees its doodads (DoodadTemplateView.ScriptCanSee).
    /// </summary>
    public static bool OverridesCanSee(Type scriptType) =>
        scriptType.IsSubclassOf(typeof(DoodadScript))
        && scriptType.GetMethod(nameof(DoodadScript.CanSee), [typeof(IDoodadViewContext)])?.DeclaringType
        != typeof(DoodadScript);

    /// <summary>
    /// Logs a throw from <paramref name="hook" /> of <paramref name="script" />, at Error, at most once per script and
    /// hook per ThrottledErrorLog.Interval. <paramref name="hook" /> is a literal, the hook's name.
    /// </summary>
    internal void Failed(Type script, string hook, Exception e)
    {
        lock (_failuresGate)
        {
            if (!_failures.TryGetValue((script, hook), out ThrottledErrorLog? log))
                _failures[(script, hook)] = log = new ThrottledErrorLog(logger, time, $"Doodad script {script.Name}.{hook}");
            log.Failed(e);
        }
    }

    private DoodadScript? Build(Type type)
    {
        try
        {
            return (DoodadScript)ActivatorUtilities.CreateInstance(_narrowed, type);
        }
        catch (Exception e)
        {
            Failed(type, "constructor", e);
            return null;
        }
    }
}
