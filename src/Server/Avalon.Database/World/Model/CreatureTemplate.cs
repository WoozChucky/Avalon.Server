namespace Avalon.Database.World.Model;

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
    public UnitClass UnitClass { get; set; }
    [Column("family")]
    public CreatureFamily Family { get; set; }
    [Column("type")]
    public CreatureType Type { get; set; }
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

public enum CreatureType : short
{
    None = 0,
    Beast = 1,
    Dragonkin = 2,
    Demon = 3,
    Elemental = 4,
    Giant = 5,
    Undead = 6,
    Humanoid = 7,
    Critter = 8,
    Mechanical = 9,
    NotSpecified = 10,
    Totem = 11,
    NonCombatPet = 12,
    GasCloud = 13,
}

public enum CreatureFamily : short
{
    None = 0,
    Wolf = 1,
    Cat = 2,
    Spider = 3,
    Bear = 4,
    Boar = 5,
    Crocolisk = 6,
    CarrionBird = 7,
    Crab = 8,
    Gorilla = 9,
    Horse = 10,
    Raptor = 11,
    Tallstrider = 12,
    Felhunter = 15,
    Voidwalker = 16,
    Succubus = 17,
    Doomguard = 19,
    Scorpid = 20,
    Turtle = 21,
    Imp = 23,
    Bat = 24,
    Hyena = 25,
    Owl = 26,
    WindSerpent = 27,
    RemoteControl = 28,
    Felguard = 29,
    Dragonhawk = 30,
    Ravager = 31,
    WarpStalker = 32,
    Sporebat = 33,
    NetherRay = 34,
    Serpent = 35,
    Moth = 37,
    Chimaera = 38,
    Devilsaur = 39,
    Ghoul = 40,
    Silithid = 41,
    Worm = 42,
    Rhino = 43,
    Wasp = 44,
    CoreHound = 45,
    SpiritBeast = 46,
}

public enum UnitClass : short
{
    Warrior = 1,
    Paladin = 2,
    Rogue = 4,
    Mage = 8,
}
