using System.ComponentModel.DataAnnotations;
using Avalon.Common;
using Avalon.Common.ValueObjects;

namespace Avalon.Domain.Characters;

public class CharacterInventory
{
    [Required]
    public Character Character { get; set; }
    public CharacterId CharacterId { get; set; }

    public InventoryType Container { get; set; }

    public byte Slot { get; set; }

    public ItemInstanceId ItemId { get; set; }
}
