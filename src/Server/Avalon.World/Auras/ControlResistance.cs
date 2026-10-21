using Avalon.Combat;
using Avalon.Domain.World;

namespace Avalon.World.Auras;

/// <summary>
/// How much of each crowd-control type's duration a creature takes (#770), in percent: 0 is immune, 100 the full
/// duration. Fixed at spawn from its template's overrides, else its rarity's row, so an application reads four numbers
/// and never the rarity table. World-side.
/// </summary>
public readonly record struct ControlResistance(float Stun, float Root, float Silence, float Slow)
{
    /// <summary>The full duration of every type: a creature with no rarity row, or one built without the spawner.</summary>
    public static readonly ControlResistance Full = new(100f, 100f, 100f, 100f);

    /// <summary>
    /// <paramref name="template" />'s own percentages where it sets them, its rarity's row in
    /// <paramref name="rarities" /> for the rest; the full duration for a type neither sets (no row for its rarity).
    /// Spawn time only: it walks the rows.
    /// </summary>
    public static ControlResistance Of(CreatureTemplate template, IEnumerable<CreatureRarityModifier> rarities)
    {
        CreatureRarityModifier? rarity = rarities.FirstOrDefault(r => r.Rarity == template.Rarity);
        return new ControlResistance(
            template.StunDurationPct ?? rarity?.StunDurationPct ?? 100f,
            template.RootDurationPct ?? rarity?.RootDurationPct ?? 100f,
            template.SilenceDurationPct ?? rarity?.SilenceDurationPct ?? 100f,
            template.SlowDurationPct ?? rarity?.SlowDurationPct ?? 100f);
    }

    /// <summary>The percentage of <paramref name="aura" />'s duration that lands: the smallest among the types it grants.</summary>
    public float PctOf(AuraTemplate aura) => CrowdControlRules.ResistPct(aura, Stun, Root, Silence, Slow);
}
