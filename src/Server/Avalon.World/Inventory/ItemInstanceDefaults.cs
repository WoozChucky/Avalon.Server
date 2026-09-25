using Avalon.Domain.World;

namespace Avalon.World.Inventory;

public static class ItemInstanceDefaults
{
    /// <summary>
    /// The durability a new instance starts with. Moved as it was from CharacterCreateHandler, so a
    /// starting item and one added in play agree; the numbers themselves predate this.
    /// </summary>
    public static uint InitialDurability(ItemTemplate template) => template.Class switch
    {
        ItemClass.Weapon => 42U,
        ItemClass.Armor => 69U,
        _ => 0U,
    };
}
