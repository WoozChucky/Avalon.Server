using System.ComponentModel.DataAnnotations;
using Avalon.Common;

namespace Avalon.Domain.World;

public class CreatureTemplate : IDbEntity<CreatureTemplateId>
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

    public short Rank { get; set; }

    public CreatureFamily Family { get; set; }

    public CreatureType Type { get; set; }

    public short Exp { get; set; }

    public int LootId { get; set; }

    public int MinGold { get; set; }

    public int MaxGold { get; set; }

    public string AIName { get; set; } = string.Empty;

    public short MovementType { get; set; }

    public float DetectionRange { get; set; }

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
}

public class CreatureTemplateId : ValueObject<ulong>
{
    public CreatureTemplateId(ulong value) : base(value) {}
    
    public static implicit operator CreatureTemplateId(ulong value)
    {
        return new CreatureTemplateId(value);
    }
}

public enum CreatureType : ushort
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

public enum CreatureFamily : ushort
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
    Humanoid = 47,
}
