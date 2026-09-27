using System.Diagnostics.CodeAnalysis;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.World.Scripts.Abilities;
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
    public static string? Problem(AbilityTemplate t)
    {
        foreach ((string name, float value) in new[]
                 {
                     ("Reach", t.Reach), ("Radius", t.Radius), ("ArcDegrees", t.ArcDegrees),
                     ("ProjectileSpeed", t.ProjectileSpeed),
                     // #529. A NaN or infinite threat value would spread into every threat total it
                     // touches, and a hostile's threat list would stop ordering anything.
                     ("ThreatMultiplier", t.ThreatMultiplier), ("HealThreatPerHp", t.HealThreatPerHp),
                 })
        {
            if (!float.IsFinite(value) || value < 0f)
                return $"{name} {value} is not a finite value of 0 or more";
        }

        // #526. A negative gain would drain the caster's pool on every hit; the database refuses it too.
        if (t.PowerGainPerHit < 0) return $"PowerGainPerHit {t.PowerGainPerHit} is below 0";

        if (!Enum.IsDefined(t.AimMode)) return $"unknown aim mode {(byte)t.AimMode}";
        if (!Enum.IsDefined(t.Shape)) return $"unknown shape {(byte)t.Shape}";
        if (!Enum.IsDefined(t.Anchor)) return $"unknown anchor {(byte)t.Anchor}";
        if (!Enum.IsDefined(t.Affects)) return $"unknown affects {(byte)t.Affects}";

        if (t.Affects == AbilityAffects.Ally && t.Shape != AbilityShape.Circle)
            return "only a circle may affect allies";

        switch (t.Shape)
        {
            case AbilityShape.Circle:
                if (ScriptMismatch(t, nameof(CircleAbilityScript), "a circle") is { } circleScript) return circleScript;
                if (t.Radius <= 0f) return "a circle needs a Radius above 0";
                if (t.Anchor == AbilityAnchor.AimPoint && t.AimMode != AbilityAimMode.Cursor)
                    return "a circle on the aim point must aim with the cursor";
                if (t.Anchor == AbilityAnchor.AimPoint && t.Reach <= 0f)
                    return "a circle on the aim point needs a Reach above 0";
                if (t.Anchor == AbilityAnchor.Caster && t.Reach > 0f)
                    return "a circle on the caster must have Reach 0";
                return null;

            case AbilityShape.Cone:
                if (ScriptMismatch(t, nameof(ConeAbilityScript), "a cone") is { } coneScript) return coneScript;
                if (t.Reach <= 0f) return "a cone needs a Reach above 0";
                if (t.ArcDegrees <= 0f || t.ArcDegrees > 360f) return "a cone needs ArcDegrees above 0 and at most 360";
                return null;

            case AbilityShape.Projectile:
                if (ScriptMismatch(t, nameof(ProjectileAbilityScript), "a projectile") is { } projectileScript)
                    return projectileScript;
                if (t.Reach <= 0f) return "a projectile needs a Reach above 0";
                if (t.ProjectileSpeed <= 0f) return "a projectile needs a ProjectileSpeed above 0";
                if (t.AimMode != AbilityAimMode.Cursor) return "a projectile must aim with the cursor";
                return null;

            default:
                return $"unknown shape {(byte)t.Shape}";
        }
    }

    /// <summary>
    /// Each shape has exactly one script that resolves it, so a row naming another would fire a shape
    /// the row's other columns were never checked for.
    /// </summary>
    private static string? ScriptMismatch(AbilityTemplate t, string expected, string shape) =>
        string.Equals(t.SpellScript, expected, StringComparison.Ordinal)
            ? null
            : $"{shape} must use {expected}, not '{t.SpellScript}'";
}
