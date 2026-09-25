using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Entities;

namespace Avalon.World.Inventory;

/// <summary>
/// Where gameplay code (loot #460, vendors #432) gets a character's inventory service and wallet.
/// Cheap per-call objects over the character's own state; templates and the money cap are read
/// live, so a reference-data reload applies to the next operation.
/// </summary>
public interface ICharacterEconomy
{
    IInventoryService InventoryOf(CharacterEntity character);

    IWallet WalletOf(CharacterEntity character);
}

public sealed class CharacterEconomy(IWorld world, IItemIdAllocator itemIds) : ICharacterEconomy
{
    public IInventoryService InventoryOf(CharacterEntity character) =>
        new CharacterInventoryService(character, FindTemplate, itemIds);

    public IWallet WalletOf(CharacterEntity character) =>
        new CharacterWallet(character, world.Configuration.MaxMoney);

    private ItemTemplate? FindTemplate(ItemTemplateId id) =>
        world.Data.ItemTemplates.FirstOrDefault(t => t.Id == id);
}
