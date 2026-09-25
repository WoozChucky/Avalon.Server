using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;

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

    public int LootId { get; set; }

    public int MinGold { get; set; }

    public int MaxGold { get; set; }

    public string AIName { get; set; } = string.Empty;

    public short MovementType { get; set; }

    public float DetectionRange { get; set; }

    /// <summary>
    /// True for creatures that can never be damaged. Town NPCs (templates 1-3) set it; every
    /// monster leaves it false. Enforced in CombatService.ApplyDamageCore.
    /// </summary>
    public bool Invulnerable { get; set; }

    public int MovementId { get; set; }

    public string ScriptName { get; set; } = string.Empty;

    public float HealthModifier { get; set; }

    public float ManaModifier { get; set; }

    public float ArmorModifier { get; set; }

    public float ExperienceModifier { get; set; }

    public short RegenHealth { get; set; }

    public short DmgSchool { get; set; }

    public float DamageModifier { get; set; }

    public int BaseAttackTime { get; set; }

    public int RangeAttackTime { get; set; }

    /// <summary>
    /// Experience awarded for this kill. <c>null</c> means derive it from <c>CreatureBaseStats</c> by
    /// level; any value, including 0, is used verbatim. Nullable rather than a 0 sentinel so a creature
    /// deliberately worth nothing stays expressible.
    /// </summary>
    [Column("Exp")]
    public uint? Experience { get; set; }

    /// <summary>
    /// Seconds before the creature re-spawns after death. Default 180 (3 minutes).
    /// </summary>
    /// <remarks>
    /// Unread since creatures stopped respawning on a timer. Kept because a deliberate revival
    /// mechanic is the intended replacement and will plausibly want a number here.
    /// </remarks>
    public int RespawnTimerSecs { get; set; } = 180;

    /// <summary>
    /// Seconds before the creature's corpse is removed from its instance. Default 10.
    /// </summary>
    /// <remarks>
    /// Short by design: a corpse is an entity the instance still ticks and broadcasts, so on a busy
    /// map long-lived corpses carpet the floor. Raise it per template for something that should
    /// linger, such as a boss.
    /// </remarks>
    public int BodyRemoveTimerSecs { get; set; } = 10;

    [NotMapped]
    public TimeSpan RespawnTimer
    {
        get => TimeSpan.FromSeconds(RespawnTimerSecs);
        set => RespawnTimerSecs = (int)value.TotalSeconds;
    }

    [NotMapped]
    public TimeSpan BodyRemoveTimer
    {
        get => TimeSpan.FromSeconds(BodyRemoveTimerSecs);
        set => BodyRemoveTimerSecs = (int)value.TotalSeconds;
    }
}
