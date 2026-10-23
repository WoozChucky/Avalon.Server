using Avalon.World.Public.Enums;
namespace Avalon.Api.Contract;

/// <summary>The body of a template edit: the template read shape without its id, version and computed fields.</summary>
public sealed class UpdateCreatureTemplateRequest
{
    public string Name { get; set; } = "";
    public string SubName { get; set; } = "";
    public string IconName { get; set; } = "";
    public short MinLevel { get; set; }
    public short MaxLevel { get; set; }
    public float SpeedWalk { get; set; }
    public float SpeedRun { get; set; }
    public float SpeedSwim { get; set; }
    public CreatureRarity Rarity { get; set; }
    public CreatureFamily Family { get; set; }
    public CreatureType Type { get; set; }
    /// <summary>Which model the game client draws this creature with; 0 is none (#1022).</summary>
    public uint DisplayId { get; set; }
    /// <summary>The loot table this creature rolls when it dies; null when it drops no items.</summary>
    public int? LootTableId { get; set; }
    public int MinGold { get; set; }
    public int MaxGold { get; set; }
    public short MovementType { get; set; }
    public float DetectionRange { get; set; }
    public int MovementId { get; set; }
    public string ScriptName { get; set; } = "";
    public float HealthModifier { get; set; }
    public float ManaModifier { get; set; }
    public float ArmorModifier { get; set; }
    public float ExperienceModifier { get; set; }
    public short RegenHealth { get; set; }
    /// <summary>The type of damage the basic attack deals, and the abilities that name none (#1178).</summary>
    public DamageType DamageType { get; set; }
    public float DamageModifier { get; set; }

    /// <summary>Percent of fire damage the creature resists, -100 to 100; negative takes more (#1178).</summary>
    public float FireResistance { get; set; }

    /// <summary>Percent of cold damage the creature resists, as <see cref="FireResistance" />.</summary>
    public float ColdResistance { get; set; }

    /// <summary>Percent of lightning damage the creature resists, as <see cref="FireResistance" />.</summary>
    public float LightningResistance { get; set; }

    /// <summary>Percent of blight damage the creature resists, as <see cref="FireResistance" />.</summary>
    public float BlightResistance { get; set; }

    /// <summary>Scales the creature's level-derived evasion; finite and 0 or more, where 0 is none (#1178).</summary>
    public float EvasionModifier { get; set; }

    /// <summary>Scales the creature's level-derived ward, as <see cref="EvasionModifier" />.</summary>
    public float WardModifier { get; set; }

    /// <summary>Seconds between the creature's swings, 0.5 or more (#627).</summary>
    public float BaseAttackTime { get; set; }
    public int RangeAttackTime { get; set; }
    /// <summary>Null means the server derives it from the creature's level.</summary>
    public uint? Experience { get; set; }

    /// <summary>Seconds before the creature's corpse is removed.</summary>
    public int BodyRemoveTimerSecs { get; set; }

    /// <summary>
    /// Percent, 0 to 100, of a stun's duration this creature takes (#770); 0 is immune. Null uses its rarity's.
    /// </summary>
    public float? StunDurationPct { get; set; }

    /// <summary>Percent of a root's duration, or null for its rarity's, as <see cref="StunDurationPct" />.</summary>
    public float? RootDurationPct { get; set; }

    /// <summary>Percent of a silence's duration, or null for its rarity's, as <see cref="StunDurationPct" />.</summary>
    public float? SilenceDurationPct { get; set; }

    /// <summary>Percent of a slow's duration, or null for its rarity's, as <see cref="StunDurationPct" />.</summary>
    public float? SlowDurationPct { get; set; }
}
