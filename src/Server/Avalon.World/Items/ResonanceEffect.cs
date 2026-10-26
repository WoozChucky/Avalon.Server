using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.World.Auras;
using Avalon.World.Public.Abilities;

namespace Avalon.World.Items;

/// <summary>
/// The Resonance triggers as flags (#1180), one bit per <see cref="ResonanceTrigger" /> (<c>1 &lt;&lt; (trigger - 1)</c>):
/// what a character's procs listen for, and what one event fired, tested against each other with one mask.
/// </summary>
[Flags]
public enum ResonanceTriggers
{
    None = 0,
    Hit = 1,
    Crit = 2,
    Kill = 4,
    Struck = 8,
    Evaded = 16,
    WardBroken = 32,
}

/// <summary>
/// One accepted Resonance effect row (#1180 part A2), built once per row by <see cref="ResonanceEffectCatalog" /> and
/// read-only: what fires it, the chance and cooldown, the aura it applies and to whom, and the ready-made
/// <see cref="AuraSource" /> it applies the aura with, so a firing builds nothing.
/// </summary>
public sealed class ResonanceEffect
{
    internal ResonanceEffect(ItemThemeEffect row, AuraTemplate aura)
    {
        Id = row.Id;
        ThemeId = row.ThemeId;
        Count = row.Count;
        Trigger = row.Trigger;
        Bit = (ResonanceTriggers)(1 << ((int)row.Trigger - 1));
        ChancePct = row.ChancePct;
        Cooldown = TimeSpan.FromMilliseconds(row.CooldownMs);
        Aura = row.AuraId;
        Target = row.Target;
        ChainTargets = row.ChainTargets;
        AtOrBelowLifePct = row.AtOrBelowLifePct;

        // The fit the aura system checks at every application: a later /reload auras that changes the aura's kind is
        // refused there, with a log, rather than placing a buff on an enemy or a debuff on the wearer.
        AbilityAffects affects = AffectsOf(row.Target);

        // A ticking aura's total is rolled from a source (OffenceMath.PeriodicTotal): a synthetic one with no spell base
        // and no tags, so the ticks scale by the wearer's increases of the row's type and of damage over time (or of
        // healing). Every effect aura has a PeriodicEffectiveness of 0, so nothing is drawn from it.
        AbilityMetadata? metadata = aura.PeriodicKind == AuraPeriodicKind.None
            ? null
            : new AbilityMetadata
            {
                Name = aura.Name,
                ScriptName = "",
                Tags = AbilityTags.None,
                Affects = affects,
                DamageType = row.DamageType,
            };

        // Threat 1 and no heal threat: a proc's damage counts as a plain hit's does, and a self-heal from gear adds none.
        Source = new AuraSource(null, 1f, 0f, affects, row.DamageType, Ability: metadata, CannotCrit: true);
    }

    public int Id { get; }

    public int ThemeId { get; }

    /// <summary>The count of the theme it needs.</summary>
    public int Count { get; }

    public ResonanceTrigger Trigger { get; }

    /// <summary><see cref="Trigger" /> as its flag.</summary>
    public ResonanceTriggers Bit { get; }

    /// <summary>Percent, above 0 and at most 100.</summary>
    public float ChancePct { get; }

    /// <summary>Above zero.</summary>
    public TimeSpan Cooldown { get; }

    public AuraId Aura { get; }

    public ResonanceTarget Target { get; }

    /// <summary>How many more units near the hit target it also applies to; above 0 only on a HitTarget effect.</summary>
    public int ChainTargets { get; }

    /// <summary>Percent: it fires only while the wearer's health is at or below this share of its maximum; null for any.</summary>
    public float? AtOrBelowLifePct { get; }

    /// <summary>What its aura is applied with: whom it fits, its damage type, its source for a ticking aura, never a crit.</summary>
    public AuraSource Source { get; }

    /// <summary>Whom an effect's aura must fit: an ally (the wearer) for a Wearer effect, a hostile unit otherwise.</summary>
    internal static AbilityAffects AffectsOf(ResonanceTarget target) =>
        target == ResonanceTarget.Wearer ? AbilityAffects.Ally : AbilityAffects.Hostile;
}
