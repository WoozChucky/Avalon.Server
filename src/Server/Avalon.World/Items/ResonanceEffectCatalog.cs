using System.Globalization;
using Avalon.Combat;
using Avalon.Domain.World;
using Avalon.World.Auras;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.World.Items;

/// <summary>
/// The Resonance effects (#1180 part A2): every effect row that passed the rules, held against the aura rows read in the
/// same items prepare (ItemsPatch.Build), so a /reload items reloads both. Built once per load, off the tick, and
/// immutable afterwards. A row that breaks a rule is logged and left out, and every other row still loads. An effect
/// aura that a later /reload auras changes is caught where it is applied, by the aura system's fit check.
/// </summary>
public sealed class ResonanceEffectCatalog
{
    private readonly ThemeEffects[] _themes;

    public ResonanceEffectCatalog(IReadOnlyCollection<ItemThemeEffect> rows, AuraCatalog auras, ILogger logger)
    {
        List<ResonanceEffect> accepted = [];
        HashSet<(int ThemeId, int Count, ResonanceTrigger Trigger)> keys = [];
        foreach (ItemThemeEffect row in rows.OrderBy(r => r.Id))
        {
            // Any earlier row's key counts, kept or not, as the table's unique index has it: which row runs for a key
            // never turns on another row's soundness.
            bool repeat = !keys.Add((row.ThemeId, row.Count, row.Trigger));
            string? reason = Refusal(row, auras) ?? (repeat ? "a second row for one theme, threshold and trigger" : null);
            if (reason is not null)
            {
                logger.LogError("Resonance effect {EffectId} is left out: {Reason}", row.Id, reason);
                continue;
            }

            auras.TryGet(row.AuraId, out AuraTemplate? aura);
            accepted.Add(new ResonanceEffect(row, aura!));
        }

        Effects = accepted;

        // Per theme, by theme id; per theme its thresholds from the highest down; per threshold its rows by id.
        _themes =
        [
            .. accepted
                .GroupBy(e => e.ThemeId)
                .OrderBy(theme => theme.Key)
                .Select(theme => new ThemeEffects(theme.Key,
                [
                    .. theme
                        .GroupBy(e => e.Count)
                        .OrderByDescending(threshold => threshold.Key)
                        .Select(threshold => new Threshold(threshold.Key, [.. threshold.OrderBy(e => e.Id)])),
                ])),
        ];
    }

    /// <summary>No effect at all: what a world sees before its first load, and what a test without effects reads.</summary>
    public static ResonanceEffectCatalog Empty { get; } = new([], AuraCatalog.Empty, NullLogger.Instance);

    /// <summary>The accepted effects, by id.</summary>
    public IReadOnlyList<ResonanceEffect> Effects { get; }

    /// <summary>
    /// Why <paramref name="row" /> cannot load against <paramref name="auras" />, or null when it can: every rule but the
    /// one between rows (a second row for one theme, threshold and trigger), which the catalog checks as it builds.
    /// </summary>
    public static string? Refusal(ItemThemeEffect row, AuraCatalog auras)
    {
        if (!Enum.IsDefined(row.Trigger))
            return $"an unknown trigger {(int)row.Trigger}";
        if (!Enum.IsDefined(row.Target))
            return $"an unknown target {(int)row.Target}";
        // An unknown type would throw from the offence maths at every placement, on the tick.
        if (row.DamageType is { } type && !Enum.IsDefined(type))
            return $"an unknown damage type {(int)type}";
        if (!auras.TryGet(row.AuraId, out AuraTemplate? aura))
            return "its aura is not loaded";

        // The same fit the aura system checks at application, so the load and every placement agree.
        bool onWearer = row.Target == ResonanceTarget.Wearer;
        if (!AuraRules.Fits(aura.Kind, ResonanceEffect.AffectsOf(row.Target)))
            return onWearer ? "a harmful aura on the wearer" : "a helpful aura on an enemy";

        // A hit target is there only for the wearer's own hit, an attacker only for a hit on the wearer; so a Kill row
        // is a Wearer row.
        if (row.Target == ResonanceTarget.HitTarget && row.Trigger is not (ResonanceTrigger.Hit or ResonanceTrigger.Crit))
            return $"a hit target on a {row.Trigger} trigger";
        if (row.Target == ResonanceTarget.Attacker
            && row.Trigger is not (ResonanceTrigger.Struck or ResonanceTrigger.Evaded or ResonanceTrigger.WardBroken))
        {
            return $"the attacker on a {row.Trigger} trigger";
        }

        if (row.ChainTargets != 0 && row.Target != ResonanceTarget.HitTarget)
            return onWearer ? "a chain on the wearer" : "a chain on the attacker";

        bool damages = aura.PeriodicKind == AuraPeriodicKind.Damage;
        if (damages && row.DamageType is null)
            return "a damaging aura without a damage type";
        if (!damages && row.DamageType is not null)
            return aura.PeriodicKind == AuraPeriodicKind.Heal ? "a damage type on a heal" : "a damage type on a status";

        if ((aura.Control & CrowdControl.Stun) != 0)
            return "an aura that stuns";
        if (!(row.ChancePct > 0f && row.ChancePct <= 100f))
            return string.Create(CultureInfo.InvariantCulture, $"a chance of {row.ChancePct}");
        if (row.CooldownMs == 0)
            return "a cooldown of 0";

        return null;
    }

    /// <summary>
    /// Appends to <paramref name="into" /> the effects <paramref name="counts" /> make active, by theme id and then
    /// effect id, and answers how many it added: for each theme, the rows of the highest threshold its count reaches, so a
    /// count above the highest runs that one's rows, and a count below the lowest runs none. Allocates nothing beyond what
    /// <paramref name="into" /> must grow by.
    /// </summary>
    public int ActiveFor(ResonanceCounts counts, List<ResonanceEffect> into)
    {
        int added = 0;
        foreach (ThemeEffects theme in _themes)
        {
            int count = counts.Of(theme.ThemeId);
            if (count <= 0)
                continue;

            foreach (Threshold threshold in theme.Thresholds)
            {
                if (count < threshold.Count)
                    continue;

                foreach (ResonanceEffect effect in threshold.Effects)
                    into.Add(effect);

                added += threshold.Effects.Length;
                break;
            }
        }

        return added;
    }

    private sealed record ThemeEffects(int ThemeId, Threshold[] Thresholds);

    private sealed record Threshold(int Count, ResonanceEffect[] Effects);
}
