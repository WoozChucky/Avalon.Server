using System.ComponentModel.DataAnnotations;
using Avalon.Common.ValueObjects;
using Avalon.World.Public.Enums;

namespace Avalon.Domain.Characters;

public class CharacterSpell
{
    [Required]
    public Character Character { get; set; }
    public CharacterId CharacterId { get; set; }
    
    public SpellId SpellId { get; set; }
    
    public uint Cooldown { get; set; } // in milliseconds (remaining time until spell is ready)
    
    // TODO: Specializations?
}
