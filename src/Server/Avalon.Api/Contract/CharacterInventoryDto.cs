using Avalon.Domain.World;
using Avalon.World.Public.Enums;

namespace Avalon.Api.Contract;

public sealed class CharacterInventoryDto
{
    public uint CharacterId { get; set; }
    public List<CharacterInventoryItemDto> Items { get; set; } = new();
}

public sealed class CharacterInventoryItemDto
{
    public Guid ItemId { get; set; }
    public InventoryType Container { get; set; }
    public ushort Slot { get; set; }
    public uint Count { get; set; }
    public uint Durability { get; set; }
    public CharacterInventoryItemTemplateDto? Template { get; set; }
}

public sealed class CharacterInventoryItemTemplateDto
{
    public ulong Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ItemRarity Rarity { get; set; }
    public uint DisplayId { get; set; }
    public ItemSlotType SlotType { get; set; }
    public ushort ItemPower { get; set; }
    public ushort RequiredLevel { get; set; }
}
