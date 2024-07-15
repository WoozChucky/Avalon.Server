using System.ComponentModel.DataAnnotations;
using Avalon.Common.ValueObjects;
using Avalon.World.Public.Enums;

namespace Avalon.Domain.Characters;

public class CharacterInventory
{
    [Required]
    public Character Character { get; set; }
    public CharacterId CharacterId { get; set; }
    
    public InventoryType Container { get; set; }
    
    public ushort Slot { get; set; }
    
    public ItemInstanceId ItemId { get; set; }
}
