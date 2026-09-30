using System.Diagnostics.CodeAnalysis;
using Avalon.Combat;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Abilities;

/// <summary>A template left out of the catalog, and why.</summary>
public sealed record AbilityRefusal(AbilityId Id, string Name, string Reason)
{
    public override string ToString() => $"ability {Id.Value} '{Name}': {Reason}";
}

/// <summary>
/// Every ability template that passed validation (#164). Built off the tick thread by the Abilities
/// reload area and immutable afterwards. A bad row is left out with an error naming it and every
/// other row still loads; a character holding a refused ability simply does not get it at select.
/// </summary>
public sealed class AbilityCatalog
{
    private readonly Dictionary<uint, AbilityTemplate> _templates = [];

    public AbilityCatalog(IReadOnlyCollection<AbilityTemplate> templates, ILoggerFactory loggerFactory)
    {
        ILogger<AbilityCatalog> logger = loggerFactory.CreateLogger<AbilityCatalog>();
        List<AbilityRefusal> refused = [];

        foreach (AbilityTemplate template in templates.OrderBy(t => t.Id.Value))
        {
            if (Problem(template) is { } reason)
            {
                refused.Add(new AbilityRefusal(template.Id, template.Name, reason));
                continue;
            }

            _templates[template.Id.Value] = template;
        }

        Refused = refused;
        Templates = _templates.Values.ToList();

        foreach (AbilityRefusal refusal in Refused)
        {
            logger.LogError("Refused ability {AbilityId} '{AbilityName}': {Reason}. Characters holding it do not get it",
                refusal.Id.Value, refusal.Name, refusal.Reason);
        }

        logger.LogInformation("Loaded {Count} abilities; refused {RefusedCount}", Count, Refused.Count);
    }

    public int Count => _templates.Count;

    public IReadOnlyList<AbilityTemplate> Templates { get; }

    public IReadOnlyList<AbilityRefusal> Refused { get; }

    public bool TryGet(AbilityId id, [NotNullWhen(true)] out AbilityTemplate? template) =>
        _templates.TryGetValue(id.Value, out template);

    public string Describe() => $"{Count} abilities, {Refused.Count} refused";

    /// <summary>Why a row cannot load, or null when it can.</summary>
    public static string? Problem(AbilityTemplate t) => AbilityRules.Problem(t);
}
