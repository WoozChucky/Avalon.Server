using System.Diagnostics.CodeAnalysis;
using Avalon.Combat;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.World.Auras;

/// <summary>
/// Every aura row that passed validation. Built off the tick thread by the Auras reload area (and by the Abilities one,
/// which checks each ability's aura against the rows it read) and immutable afterwards. A bad row is left out with an
/// error naming it and every other row still loads. Forward-only: an aura already on a unit keeps the template it was
/// applied with; one whose id this catalog no longer holds expires on its next tick.
/// </summary>
public sealed class AuraCatalog
{
    private readonly Dictionary<uint, AuraTemplate> _templates = [];

    /// <summary>No aura at all: what a world sees before its first load, and what a test without auras reads.</summary>
    public static readonly AuraCatalog Empty = new([], static _ => null, NullLoggerFactory.Instance);

    /// <param name="findScript">The AuraScript type a script name names, or null: ScriptManager.GetAuraScript.</param>
    public AuraCatalog(IReadOnlyCollection<AuraTemplate> templates, Func<string, Type?> findScript, ILoggerFactory loggerFactory)
    {
        ILogger<AuraCatalog> logger = loggerFactory.CreateLogger<AuraCatalog>();
        List<AuraRefusal> refused = [];

        foreach (AuraTemplate template in templates.OrderBy(t => t.Id.Value))
        {
            string? reason = AuraRules.Problem(template)
                             ?? (template.ScriptName is { Length: > 0 } script && findScript(script) is null
                                 ? $"names aura script '{script}', which is not loaded"
                                 : null);
            if (reason is not null)
            {
                refused.Add(new AuraRefusal(template.Id, template.Name, reason));
                continue;
            }

            _templates[template.Id.Value] = template;
        }

        Refused = refused;
        Templates = _templates.Values.ToList();

        foreach (AuraRefusal refusal in Refused)
        {
            logger.LogError("Refused aura {AuraId} '{AuraName}': {Reason}. Nothing applies it", refusal.Id.Value,
                refusal.Name, refusal.Reason);
        }

        if (templates.Count > 0)
            logger.LogInformation("Loaded {Count} auras; refused {RefusedCount}", Count, Refused.Count);
    }

    public int Count => _templates.Count;

    public IReadOnlyList<AuraTemplate> Templates { get; }

    public IReadOnlyList<AuraRefusal> Refused { get; }

    public bool TryGet(AuraId id, [NotNullWhen(true)] out AuraTemplate? template) =>
        _templates.TryGetValue(id.Value, out template);

    public string Describe() => $"{Count} auras, {Refused.Count} refused";
}
