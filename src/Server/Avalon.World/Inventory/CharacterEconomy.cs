using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Loot;

namespace Avalon.World.Inventory;

/// <summary>
/// Where gameplay code (loot #460, vendors #432) gets a character's inventory service, wallet and
/// vendor trade. Cheap objects over the character's own state (the inventory service is kept on the character, the
/// wallet and the trade are made per call); templates, the modifier catalog (#1179) and the gold cap are read live, so a
/// reference-data reload applies to the next operation.
/// </summary>
public interface ICharacterEconomy
{
    IInventoryService InventoryOf(CharacterEntity character);

    IWallet WalletOf(CharacterEntity character);

    /// <summary>The vendor service operations over this character's inventory and wallet (spec #432).</summary>
    VendorTrade VendorOf(CharacterEntity character);
}

public sealed class CharacterEconomy : ICharacterEconomy
{
    private readonly IWorld _world;
    private readonly IItemIdAllocator _itemIds;
    private readonly IItemRoller _roller;

    // Made once: handing the method itself to each service made a delegate per call (#883). They read the item
    // templates and the modifier catalog as they are when called, so a reload still applies to the next operation.
    private readonly Func<ItemTemplateId, ItemTemplate?> _findTemplate;
    private readonly Func<ItemModifierCatalog> _currentCatalog;

    public CharacterEconomy(IWorld world, IItemIdAllocator itemIds, IItemRoller roller)
    {
        _world = world;
        _itemIds = itemIds;
        _roller = roller;
        _findTemplate = FindTemplate;
        _currentCatalog = CurrentCatalog;
    }

    /// <summary>
    /// The character's one inventory service from this economy, made on first use and kept on the character (#883):
    /// it is asked for per item request, pickup, vendor trade and quest reward, and holds nothing but the character,
    /// the template and catalog lookups, the id allocator and the roller. A character another economy made one for gets
    /// its own from this one.
    /// </summary>
    public IInventoryService InventoryOf(CharacterEntity character)
    {
        (ICharacterEconomy? economy, IInventoryService? kept) = character.EconomyInventory;
        if (ReferenceEquals(economy, this) && kept is not null)
            return kept;

        var service = new CharacterInventoryService(character, _findTemplate, _itemIds, _roller, _currentCatalog);
        character.EconomyInventory = (this, service);
        return service;
    }

    public IWallet WalletOf(CharacterEntity character) =>
        new CharacterWallet(character, _world.Configuration.MaxGold);

    public VendorTrade VendorOf(CharacterEntity character) =>
        new(character, InventoryOf(character), WalletOf(character), _findTemplate, _world.Configuration.MaxGold,
            _world.Configuration.Items.SellFactor);

    private ItemTemplate? FindTemplate(ItemTemplateId id) =>
        _world.Data.FindItemTemplate(id);

    private ItemModifierCatalog CurrentCatalog() => _world.Data.ItemModifiers;
}
