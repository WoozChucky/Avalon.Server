namespace Avalon.Database.World;

public class CreatureTemplate
{
    [Column("id")]
    public int Id { get; set; }
    [Column("name")]
    public string Name { get; set; } = string.Empty;
    [Column("subname")]
    public string SubName { get; set; } = string.Empty;
    [Column("IconName")]
    public string IconName { get; set; } = string.Empty;
    [Column("minlevel")]
    public short MinLevel { get; set; }
    [Column("maxlevel")]
    public short MaxLevel { get; set; }
    [Column("speed_walk")]
    public float SpeedWalk { get; set; }
    [Column("speed_run")]
    public float SpeedRun { get; set; }
    [Column("speed_swim")]
    public float SpeedSwim { get; set; }
    [Column("rank")]
    public short Rank { get; set; }
    [Column("unit_class")]
    public short UnitClass { get; set; }
    [Column("family")]
    public short Family { get; set; }
    [Column("type")]
    public short Type { get; set; }
    [Column("exp")]
    public short Exp { get; set; }
    [Column("lootid")]
    public int LootId { get; set; }
    [Column("mingold")]
    public int MinGold { get; set; }
    [Column("maxgold")]
    public int MaxGold { get; set; }
    [Column("AIName")]
    public string AiName { get; set; } = string.Empty;
    [Column("MovementType")]
    public short MovementType { get; set; }
    [Column("detection_range")]
    public float DetectionRange { get; set; }
    [Column("movementId")]
    public int MovementId { get; set; }
    [Column("ScriptName")]
    public string ScriptName { get; set; } = string.Empty;
    [Column("HealthModifier")]
    public float HealthModifier { get; set; }
    [Column("ManaModifier")]
    public float ManaModifier { get; set; }
    [Column("ArmorModifier")]
    public float ArmorModifier { get; set; }
    [Column("ExperienceModifier")]
    public float ExperienceModifier { get; set; }
    [Column("RegenHealth")]
    public short RegenHealth { get; set; }
    [Column("dmgschool")]
    public short DmgSchool { get; set; }
    [Column("DamageModifier")]
    public float DamageModifier { get; set; }
    [Column("BaseAttackTime")]
    public int BaseAttackTime { get; set; }
    [Column("RangeAttackTime")]
    public int RangeAttackTime { get; set; }
}
