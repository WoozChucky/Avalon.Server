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
}
