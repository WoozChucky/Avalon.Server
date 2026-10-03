using Avalon.Domain.World;
using Avalon.World.Quests;
using Avalon.World.Scripts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Auras;

/// <summary>
/// Builds and runs aura scripts: each type once, through QuestScriptServices, so a script can obtain loggers and the
/// clock and nothing that writes. A missing or unbuildable script is logged and its aura runs without one. Every hook is
/// contained, its failures logged at most once per aura and hook per ThrottledErrorLog.Interval. Tick thread only; a DI
/// singleton.
/// </summary>
public sealed class AuraScripts(IScriptManager scripts, IServiceProvider services, TimeProvider time, ILogger<AuraScripts> logger)
{
    private readonly QuestScriptServices _narrowed = new(services);
    private readonly Dictionary<Type, AuraScript?> _built = [];
    private readonly Dictionary<string, ThrottledErrorLog> _failures = new(StringComparer.Ordinal);

    /// <summary>The script the aura names, built once per type; null with no name, an unknown name or an unbuildable type.</summary>
    public AuraScript? For(AuraTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.ScriptName))
            return null;

        Type? type = scripts.GetAuraScript(template.ScriptName);
        if (type is null)
        {
            Fail($"aura {template.Id.Value} script {template.ScriptName} (no AuraScript has that name)",
                new InvalidOperationException($"No AuraScript is called '{template.ScriptName}'."));
            return null;
        }

        if (_built.TryGetValue(type, out AuraScript? built))
            return built;

        try
        {
            built = (AuraScript)ActivatorUtilities.CreateInstance(_narrowed, type);
        }
        catch (Exception e)
        {
            Fail($"aura script {type.Name} (cannot be built)", e);
            built = null;
        }

        _built[type] = built;
        return built;
    }

    /// <summary>Runs one hook of the aura's script, if it has one; a throw is logged and swallowed.</summary>
    public void Run(AuraTemplate template, string hook, Action<AuraScript> call)
    {
        if (For(template) is not { } script)
            return;

        try
        {
            call(script);
        }
        catch (Exception e)
        {
            Fail($"aura {template.Id.Value} script {template.ScriptName}.{hook}", e);
        }
    }

    private void Fail(string step, Exception e)
    {
        if (!_failures.TryGetValue(step, out ThrottledErrorLog? log))
            _failures[step] = log = new ThrottledErrorLog(logger, time, step);
        log.Failed(e);
    }
}
