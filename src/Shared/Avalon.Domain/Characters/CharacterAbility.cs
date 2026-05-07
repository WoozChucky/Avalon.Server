using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Avalon.Common.ValueObjects;

namespace Avalon.Domain.Characters;

[Table("CharacterSpells")]
public class CharacterAbility
{
    [Required]
    public Character Character { get; set; }
    public CharacterId CharacterId { get; set; }

    [Column("SpellId")]
    public AbilityId AbilityId { get; set; }

    public uint Cooldown { get; set; } // in milliseconds (remaining time until spell is ready)

    // TODO: Specializations?
}
