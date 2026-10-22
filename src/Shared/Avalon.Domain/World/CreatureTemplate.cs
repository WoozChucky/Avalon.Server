using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;

namespace Avalon.Domain.World;

public class CreatureTemplate : IDbEntity<CreatureTemplateId>, ICreatureMetadata
{
    [Key]
    [Required]
    public CreatureTemplateId Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string SubName { get; set; } = string.Empty;

    public string IconName { get; set; } = string.Empty;

    public short MinLevel { get; set; }

    public short MaxLevel { get; set; }

    public float SpeedWalk { get; set; }

    public float SpeedRun { get; set; }

    public float SpeedSwim { get; set; }

    /// <summary>
    /// Scales this creature's derived stats. Replaces the former <c>Rank</c> column, which was always
    /// 0 and read nowhere — it was this concept, left unfinished.
    /// </summary>
    public CreatureRarity Rarity { get; set; }

    public CreatureFamily Family { get; set; }

    public CreatureType Type { get; set; }

    /// <summary>
    /// Which model a client draws this creature with (#1022). It names a model, not a template: the models are
    /// numbered from 1 in the game client's model list (its <c>CreatureModels.h</c>), and every template that
    /// looks alike carries the same one. 0 is none: the client draws its default. Fixed on the creature at spawn
    /// and sent as <c>ObjectState.DisplayId</c>.
    /// </summary>
    public uint DisplayId { get; set; }

    /// <summary>
    /// What this creature drops when it dies. Null drops no items; gold still comes from
    /// <see cref="MinGold"/> and <see cref="MaxGold"/>.
    /// </summary>
    public LootTableId? LootTableId { get; set; }

    /// <summary>The smallest gold pile this creature drops.</summary>
    public int MinGold { get; set; }

    /// <summary>Gold. With <see cref="MinGold"/> also 0, the creature drops no pile.</summary>
    public int MaxGold { get; set; }

    public short MovementType { get; set; }

    public float DetectionRange { get; set; }

    /// <summary>
    /// True for creatures that can never be damaged. Town NPCs (templates 1-3) set it; every
    /// monster leaves it false. Enforced in CombatService.ApplyDamageCore.
    /// </summary>
    public bool Invulnerable { get; set; }

    /// <summary>
    /// Metres. The radius of this creature's body circle for skill hit tests (#164). Above 0; a check
    /// constraint enforces it.
    /// </summary>
    public float BodyRadius { get; set; } = UnitBody.DefaultCreatureRadius;

    public int MovementId { get; set; }

    public string ScriptName { get; set; } = string.Empty;

    public float HealthModifier { get; set; }

    public float ManaModifier { get; set; }

    public float ArmorModifier { get; set; }

    public float ExperienceModifier { get; set; }

    public short RegenHealth { get; set; }

    /// <summary>
    /// The type of damage the creature's basic attack deals, and its abilities that name none (#1178). Stored in the
    /// column <c>DmgSchool</c>, which the world never read before.
    /// </summary>
    public DamageType DamageType { get; set; }

    public float DamageModifier { get; set; }

    /// <summary>Percent, -100 to 100, of fire damage the creature resists (#1178); negative takes more. Not rarity-scaled.</summary>
    public float FireResistance { get; set; }

    /// <summary>Percent of cold damage the creature resists, as <see cref="FireResistance" />.</summary>
    public float ColdResistance { get; set; }

    /// <summary>Percent of lightning damage the creature resists, as <see cref="FireResistance" />.</summary>
    public float LightningResistance { get; set; }

    /// <summary>Percent of blight damage the creature resists, as <see cref="FireResistance" />.</summary>
    public float BlightResistance { get; set; }

    /// <summary>
    /// Scales the level-derived base evasion (#1178). Finite and 0 or more; 0 gives none, unlike
    /// <see cref="ArmorModifier" />, where 0 or less counts as 1.
    /// </summary>
    public float EvasionModifier { get; set; }

    /// <summary>Scales the level-derived base ward, as <see cref="EvasionModifier" />.</summary>
    public float WardModifier { get; set; }

    /// <summary>
    /// Seconds between the creature's swings (#627), 0.5 or more and finite (a database check and the
    /// Creatures reload area both refuse anything else). Fixed on the creature at spawn.
    /// </summary>
    public float BaseAttackTime { get; set; } = 2.25f;

    public int RangeAttackTime { get; set; }

    /// <summary>
    /// Experience awarded for this kill. <c>null</c> means derive it from <c>CreatureBaseStats</c> by
    /// level; any value, including 0, is used verbatim. Nullable rather than a 0 sentinel so a creature
    /// deliberately worth nothing stays expressible.
    /// </summary>
    [Column("Exp")]
    public uint? Experience { get; set; }

    /// <summary>
    /// Seconds before the creature's corpse is removed from its instance. Default 10.
    /// </summary>
    /// <remarks>
    /// Short by design: a corpse is an entity the instance still ticks and broadcasts, so on a busy
    /// map long-lived corpses carpet the floor. Raise it per template for something that should
    /// linger, such as a boss.
    /// </remarks>
    public int BodyRemoveTimerSecs { get; set; } = 10;

    /// <summary>
    /// Percent, 0 to 100, of a stun's duration this creature takes (#770), or null for its rarity's
    /// (<c>CreatureRarityModifiers</c>). How one boss is stunnable while the others are not.
    /// </summary>
    public float? StunDurationPct { get; set; }

    /// <summary>Percent of a root's duration, or null for its rarity's, as <see cref="StunDurationPct" />.</summary>
    public float? RootDurationPct { get; set; }

    /// <summary>Percent of a silence's duration, or null for its rarity's, as <see cref="StunDurationPct" />.</summary>
    public float? SilenceDurationPct { get; set; }

    /// <summary>Percent of a slow's duration, or null for its rarity's, as <see cref="StunDurationPct" />.</summary>
    public float? SlowDurationPct { get; set; }

    [NotMapped]
    public TimeSpan BodyRemoveTimer
    {
        get => TimeSpan.FromSeconds(BodyRemoveTimerSecs);
        set => BodyRemoveTimerSecs = (int)value.TotalSeconds;
    }
}
