using System.ComponentModel.DataAnnotations;
using Avalon.Common.ValueObjects;
using Avalon.World.Public.Enums;

namespace Avalon.Domain.Characters;

public class CharacterStats
{
    [Required]
    public Character Character { get; set; }
    public CharacterId CharacterId { get; set; }
    public uint MaxHealth { get; set; }
    public uint MaxPower1 { get; set; }
    public uint MaxPower2 { get; set; }
    public uint Stamina { get; set; }
    public uint Strength { get; set; }
    public uint Agility { get; set; }
    public uint Intellect { get; set; }
    public uint Armor { get; set; }
    public float BlockPct { get; set; }
    public float DodgePct { get; set; }
    public float CritPct { get; set; }
    public uint AttackDamage { get; set; }
    public uint AbilityDamage { get; set; }

    public static uint GetBaseHp(CharacterClass @class, uint stamina, ushort level)
    {
        var baseHp = level * 20;

        return @class switch
        {
            CharacterClass.Warrior => (uint)baseHp + (stamina * 10),
            CharacterClass.Wizard => (uint)baseHp + (stamina * 5),
            CharacterClass.Hunter => (uint)baseHp + (stamina * 8),
            CharacterClass.Healer => (uint)baseHp + (stamina * 7),
            _ => 0
        };
    }

    public static uint GetBasePower(CharacterClass @class, uint intellect, uint agility, uint level)
    {
        var basePower = level * 10;

        return @class switch
        {
            CharacterClass.Warrior => 100,
            CharacterClass.Wizard => basePower + (intellect * 15),
            CharacterClass.Hunter => basePower + (uint)((agility * 0.8) + (intellect * 0.2) * 10),
            CharacterClass.Healer => basePower + (intellect * 12),
            _ => 0
        };
    }

    public static float GetBaseBlockPercent(CharacterClass @class)
    {
        return @class switch
        {
            CharacterClass.Warrior => 5.0f,
            CharacterClass.Wizard => 0.0f,
            CharacterClass.Hunter => 0.0f,
            CharacterClass.Healer => 0.0f,
            _ => 0.0f
        };
    }

    public static float GetBaseDodgePercent(CharacterClass @class)
    {
        return @class switch
        {
            CharacterClass.Warrior => 3.664f,
            CharacterClass.Wizard => 3.25f,
            CharacterClass.Hunter => 4.35f,
            CharacterClass.Healer => 3.25f,
            _ => 0.0f
        };
    }

    public static float GetBaseCritPercent(CharacterClass @class)
    {
        return @class switch
        {
            CharacterClass.Warrior => 5.0f,
            CharacterClass.Wizard => 1.85f,
            CharacterClass.Hunter => 5.0f,
            CharacterClass.Healer => 1.85f,
            _ => 0.0f
        };
    }

    public static uint GetBaseAttackDamage(CharacterClass @class, uint strength, uint agility)
    {
        return @class switch
        {
            CharacterClass.Warrior => (uint)(strength * 2),
            CharacterClass.Wizard => (uint)(strength * 0.5),
            CharacterClass.Hunter => (uint)(strength * 0.5 + agility * 1.5),
            CharacterClass.Healer => (uint)(strength * 0.5),
            _ => 0
        };
    }

    public static uint GetBaseAbilityDamage(CharacterClass @class, uint intellect)
    {
        return @class switch
        {
            CharacterClass.Warrior => (uint)(intellect * 0.2),
            CharacterClass.Wizard => (uint)(intellect * 3),
            CharacterClass.Hunter => (uint)(intellect * 0.5),
            CharacterClass.Healer => (uint)(intellect * 2),
            _ => 0
        };
    }
}
