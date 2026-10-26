using Avalon.Common;

namespace Avalon.Api.Contract;

/// <summary>The body of a template edit: the template read shape without its id, version and computed fields.</summary>
public sealed class UpdateAbilityTemplateRequest
{
    public string Name { get; set; } = "";

    /// <summary>Cast time in milliseconds.</summary>
    public uint CastTime { get; set; }

    /// <summary>Cooldown in milliseconds.</summary>
    public uint Cooldown { get; set; }

    /// <summary>Cost in mana.</summary>
    public uint Cost { get; set; }

    public string ScriptName { get; set; } = "";
    public SpellRange Range { get; set; }
    public SpellEffect Effects { get; set; }
    public List<CharacterClass> AllowedClasses { get; set; } = [];

    /// <summary>The aura each unit the ability affects receives, or null (auras).</summary>
    public uint? AuraId { get; set; }

    /// <summary>
    /// The type of damage the ability deals (#1178); null for one that deals none, or a creature's own ability that deals
    /// its creature's type. A class ability that deals damage names one, and an Ally ability never does.
    /// </summary>
    public DamageType? DamageType { get; set; }

    /// <summary>
    /// What the ability is (#1178): at most one of Attack, Spell and Heal, plus Area, Projectile and Melee. Its damage and
    /// healing scale by these tags.
    /// </summary>
    public AbilityTags Tags { get; set; }

    /// <summary>
    /// Percent (#1178). An Attack deals its weapon's damage times this; a Spell's or a Heal's own base ignores it and
    /// only added damage is scaled by it. Finite and 0 or more, above 0 for an Attack that deals damage.
    /// </summary>
    public float DamageEffectiveness { get; set; }

    /// <summary>The lowest amount a Spell or a Heal rolls at level 1 (#1178); 0 for an Attack.</summary>
    public uint SpellBaseMin { get; set; }

    /// <summary>The highest amount a Spell or a Heal rolls at level 1 (#1178), not below the minimum; above 0 for one that deals or heals.</summary>
    public uint SpellBaseMax { get; set; }

    /// <summary>Percent of the level-1 spell base added per level above 1 (#1178), linear. Finite and 0 or more.</summary>
    public float SpellBasePerLevelPct { get; set; }

    /// <summary>The element an Attack converts part of its weapon's physical damage to (#1178): Fire, Cold, Lightning or Blight; null for none.</summary>
    public DamageType? ConvertToType { get; set; }

    /// <summary>Percent, 0 to 100, of the weapon's physical damage converted to <see cref="ConvertToType"/> (#1178); 0 converts nothing.</summary>
    public float ConvertPct { get; set; }

    /// <summary>The type of the ability's own added damage (#1178); null for none, named whenever <see cref="AddedMax"/> is above 0.</summary>
    public DamageType? AddedType { get; set; }

    /// <summary>The lowest added damage the ability rolls (#1178), before its damage effectiveness.</summary>
    public uint AddedMin { get; set; }

    /// <summary>The highest added damage the ability rolls (#1178), not below the minimum; 0 for none.</summary>
    public uint AddedMax { get; set; }
}
