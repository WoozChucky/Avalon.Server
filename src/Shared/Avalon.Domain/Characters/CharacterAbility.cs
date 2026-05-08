using System.ComponentModel.DataAnnotations;
using Avalon.Common.ValueObjects;

namespace Avalon.Domain.Characters;

public class CharacterAbility
{
    [Required]
    public Character Character { get; set; }
    public CharacterId CharacterId { get; set; }

    public AbilityId AbilityId { get; set; }

    public uint Cooldown { get; set; } // in milliseconds (remaining time until spell is ready)

    // TODO: Specializations?
}
