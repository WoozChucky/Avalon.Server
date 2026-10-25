using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Combat;

namespace Avalon.Domain.World;

/// <summary>
/// One Resonance effect (#1180): what a theme does once a character's count of it reaches <see cref="Count" />. On its
/// <see cref="Trigger" />, it applies its aura to its <see cref="Target" /> with a chance, then waits out its cooldown.
/// A balance table, seeded from ResonanceSeed.Effects; the id is a plain surrogate. A theme runs only the rows of the
/// highest count its count reaches.
/// </summary>
public class ItemThemeEffect
{
    public int Id { get; set; }

    /// <summary>The <see cref="ItemTheme" /> it belongs to.</summary>
    public int ThemeId { get; set; }

    /// <summary>The count of the theme it needs, 1 or more.</summary>
    public int Count { get; set; }

    public ResonanceTrigger Trigger { get; set; }

    /// <summary>Percent, above 0 and at most 100: the chance it fires on its trigger.</summary>
    public float ChancePct { get; set; }

    /// <summary>Milliseconds, above 0: how long it waits after it fires before it can fire again.</summary>
    public uint CooldownMs { get; set; }

    /// <summary>The aura it applies.</summary>
    public AuraId AuraId { get; set; } = null!;

    public ResonanceTarget Target { get; set; }

    /// <summary>How many more units near its hit target it also applies to; 0 for most, and only on a HitTarget row.</summary>
    public int ChainTargets { get; set; }

    /// <summary>Percent: it fires only while the wearer's health is at or below this share of its maximum; null for any.</summary>
    public float? AtOrBelowLifePct { get; set; }

    /// <summary>The damage type of the aura's ticks, exactly when its aura deals damage; null otherwise.</summary>
    public DamageType? DamageType { get; set; }
}
