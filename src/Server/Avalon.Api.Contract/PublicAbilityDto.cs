using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

/// <summary>
/// An ability as a tooltip shows it to anyone: no script, threat, shape or projectile fields, but how many units it hits.
/// </summary>
public sealed class PublicAbilityDto
{
    public uint Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Cost in mana.</summary>
    public uint Cost { get; set; }
    public SpellRange Range { get; set; }
    /// <summary>In milliseconds.</summary>
    public uint CastTime { get; set; }
    /// <summary>In milliseconds.</summary>
    public uint Cooldown { get; set; }
    /// <summary>
    /// Whether the talent tree grants it (#1181): a class ability, whose Attack deals its wielder's weapon damage. One the
    /// tree does not grant is a creature's, dealing its natural damage.
    /// </summary>
    public bool Granted { get; set; }
    /// <summary>The most units it hits, the nearest it may (#1181); 0 is no limit.</summary>
    public int MaxTargets { get; set; }
    /// <summary>The type of damage the ability deals, or null when it names none (#1178).</summary>
    public DamageType? DamageType { get; set; }
    /// <summary>What the ability is (#1178): at most one of Attack, Spell and Heal, plus Area, Projectile and Melee.</summary>
    public AbilityTags Tags { get; set; }
    /// <summary>Percent of its weapon's damage an Attack deals; for a Spell or a Heal, what its added damage is scaled by (#1178).</summary>
    public float DamageEffectiveness { get; set; }
    /// <summary>The lowest amount a Spell or a Heal rolls at level 1 (#1178).</summary>
    public uint SpellBaseMin { get; set; }
    /// <summary>The highest amount a Spell or a Heal rolls at level 1 (#1178).</summary>
    public uint SpellBaseMax { get; set; }
    /// <summary>Percent of the level-1 spell base added per level above 1 (#1178).</summary>
    public float SpellBasePerLevelPct { get; set; }
    /// <summary>The element an Attack converts part of its weapon's physical damage to, or null (#1178).</summary>
    public DamageType? ConvertToType { get; set; }
    /// <summary>Percent of the weapon's physical damage converted (#1178).</summary>
    public float ConvertPct { get; set; }
    /// <summary>The type of the ability's own added damage, or null for none (#1178).</summary>
    public DamageType? AddedType { get; set; }
    /// <summary>The lowest added damage the ability rolls (#1178).</summary>
    public uint AddedMin { get; set; }
    /// <summary>The highest added damage the ability rolls; 0 for none (#1178).</summary>
    public uint AddedMax { get; set; }
    /// <summary>None when the ability deals or heals no direct amount.</summary>
    public AbilityAmountKind AmountKind { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AbilityAmountKind { None, Damage, Healing }
