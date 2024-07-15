using System.ComponentModel.DataAnnotations;
using Avalon.Common.ValueObjects;
using Avalon.World.Public.Enums;

namespace Avalon.Domain.World;

public class SpellTemplate : IDbEntity<SpellId>
{
    [Key]
    public SpellId Id { get; set; }
    
    public string Name { get; set; }

    public uint CastTime { get; set; } // in milliseconds
    
    public uint Cooldown { get; set; } // in milliseconds
    
    public uint Cost { get; set; } // in power points (mana or fury)
    
    public SpellRange Range { get; set; } // in meters
    
    public SpellEffect Effects { get; set; }
    
    public uint EffectValue { get; set; }
    
    public List<CharacterClass> AllowedClasses { get; set; } = []; // Default to no classes
}
