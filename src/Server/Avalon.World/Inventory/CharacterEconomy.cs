using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Loot;
using Avalon.World.Reload;

namespace Avalon.World.Inventory;

/// <summary>
/// Where gameplay code (loot #460, vendors #432) gets a character's inventory service, wallet and
/// vendor trade. Cheap objects over the character's own state (the inventory service and the wallet are kept on the
/// character, the trade is made per call); templates, the modifier catalog (#1179) and the gold cap are read live, so a
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
    private readonly Func<ItemsPatch> _currentItems;

    public CharacterEconomy(IWorld world, IItemIdAllocator itemIds, IItemRoller roller)
    {
        _world = world;
        _itemIds = itemIds;
        _roller = roller;
        _findTemplate = FindTemplate;
        _currentItems = CurrentItems;
    }

    /// <summary>
    /// The character's one inventory service from this economy, made on first use and kept on the character (#883):
    /// it is asked for per item request, pickup, vendor trade and quest reward, and holds nothing but the character,
    /// the template and items lookups, the id allocator and the roller. A character another economy made one for gets
    /// its own from this one.
    /// </summary>
    public IInventoryService InventoryOf(CharacterEntity character)
    {
        (ICharacterEconomy? economy, IInventoryService? kept) = character.EconomyInventory;
        if (ReferenceEquals(economy, this) && kept is not null)
            return kept;

        var service = new CharacterInventoryService(character, _findTemplate, _itemIds, _roller, _currentItems);
        character.EconomyInventory = (this, service);
        return service;
    }

    /// <summary>
    /// The character's one wallet from this economy, made on first use and kept on the character (#1179), as the
    /// inventory service is: an identify request at a cost asks for it every time, refused or not. It holds the gold cap
    /// it was made under, so a cap read differently now makes a new one.
    /// </summary>
    public IWallet WalletOf(CharacterEntity character)
    {
        ulong maxGold = _world.Configuration.MaxGold;
        (ICharacterEconomy? economy, CharacterWallet? kept) = character.EconomyWallet;
        if (ReferenceEquals(economy, this) && kept is not null && kept.MaxGold == maxGold)
            return kept;

        var wallet = new CharacterWallet(character, maxGold);
        character.EconomyWallet = (this, wallet);
        return wallet;
    }

    public VendorTrade VendorOf(CharacterEntity character) =>
        new(character, InventoryOf(character), WalletOf(character), _findTemplate, _world.Configuration.MaxGold,
            _world.Configuration.Items);

    private ItemTemplate? FindTemplate(ItemTemplateId id) =>
        _world.Data.FindItemTemplate(id);

    private ItemsPatch CurrentItems() => _world.Data.Items;
}
