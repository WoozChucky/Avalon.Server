using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Vendors;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.Vendors;

/// <summary>
/// A vendor's stock and the items it sells, for the vendor tests. Every price is non-zero and
/// distinct, so a test that reads the wrong one fails. The Smith (template 12) sells five rows;
/// the Pedlar (template 13) sells one.
/// </summary>
internal static class VendorTestData
{
    public static readonly CreatureTemplateId Smith = new(12);
    public static readonly CreatureTemplateId Pedlar = new(13);

    public static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Stacks to 20. Never damaged: a consumable's full durability is 0.</summary>
    public static readonly ItemTemplate Tonic = new()
    {
        Id = new ItemTemplateId(700), Name = "Tonic", Class = ItemClass.Consumable, SubClass = ItemSubClass.Potion,
        MaxStackSize = 20, BuyPrice = 10, SellPrice = 4,
    };

    /// <summary>A weapon: full durability 42.</summary>
    public static readonly ItemTemplate Blade = new()
    {
        Id = new ItemTemplateId(701), Name = "Blade", Class = ItemClass.Weapon, SubClass = ItemSubClass.OneHanded,
        MaxStackSize = 1, Slot = ItemSlotType.MainHand, BuyPrice = 100, SellPrice = 25,
    };

    /// <summary>Stacks to 5; the Smith sells it for gold plus two Tonics each.</summary>
    public static readonly ItemTemplate Elixir = new()
    {
        Id = new ItemTemplateId(702), Name = "Elixir", Class = ItemClass.Consumable, SubClass = ItemSubClass.Potion,
        MaxStackSize = 5, BuyPrice = 30, SellPrice = 8,
    };

    /// <summary>Unique; the Smith sells it at an override of 40, not its BuyPrice.</summary>
    public static readonly ItemTemplate Charm = new()
    {
        Id = new ItemTemplateId(703), Name = "Charm", Class = ItemClass.Quest, SubClass = ItemSubClass.QuestItem,
        MaxStackSize = 1, Flags = ItemTemplateFlags.Unique, BuyPrice = 50, SellPrice = 12,
    };

    public static readonly ItemTemplate Keepsake = new()
    {
        Id = new ItemTemplateId(704), Name = "Keepsake", Class = ItemClass.Junk, SubClass = ItemSubClass.JunkItem,
        MaxStackSize = 1, Flags = ItemTemplateFlags.NoSell, BuyPrice = 5, SellPrice = 5,
    };

    /// <summary>Sells for nothing.</summary>
    public static readonly ItemTemplate Trinket = new()
    {
        Id = new ItemTemplateId(705), Name = "Trinket", Class = ItemClass.Junk, SubClass = ItemSubClass.JunkItem,
        MaxStackSize = 1, SellPrice = 0,
    };

    /// <summary>Armour: full durability 69. Sold only behind quest 7, completed.</summary>
    public static readonly ItemTemplate Plate = new()
    {
        Id = new ItemTemplateId(706), Name = "Plate", Class = ItemClass.Armor, SubClass = ItemSubClass.Chest,
        MaxStackSize = 1, Slot = ItemSlotType.Chest, BuyPrice = 80, SellPrice = 20,
    };

    public const uint TonicSequence = 1;
    public const uint BladeSequence = 2;
    public const uint ElixirSequence = 3;
    public const uint CharmSequence = 4;
    public const uint GatedSequence = 5;
    public const uint GatedQuest = 7;

    public static IReadOnlyCollection<ItemTemplate> Items => [Tonic, Blade, Elixir, Charm, Keepsake, Trinket, Plate];

    private static readonly Dictionary<ItemTemplateId, ItemTemplate> ById =
        new[] { Tonic, Blade, Elixir, Charm, Keepsake, Trinket, Plate }.ToDictionary(t => t.Id);

    public static ItemTemplate? Find(ItemTemplateId id) => ById.GetValueOrDefault(id);

    /// <summary>
    /// Fresh rows on every call, so a test can change them:
    /// <list type="bullet">
    /// <item>row 1, Tonic, unlimited;</item>
    /// <item>row 2, Blade, 2 restocking every 60 s;</item>
    /// <item>row 3, Elixir, 5 restocking every 600 s, costing 2 Tonics each;</item>
    /// <item>row 4, Charm, at 40;</item>
    /// <item>row 5, Plate, behind quest 7 completed;</item>
    /// <item>row 10, the Pedlar's Tonic.</item>
    /// </list>
    /// </summary>
    public static List<VendorStock> Rows() =>
    [
        new() { Id = 1, CreatureTemplateId = Smith, Sequence = TonicSequence, ItemTemplateId = Tonic.Id },
        new() { Id = 2, CreatureTemplateId = Smith, Sequence = BladeSequence, ItemTemplateId = Blade.Id, MaxStock = 2, RestockSeconds = 60 },
        new()
        {
            Id = 3, CreatureTemplateId = Smith, Sequence = ElixirSequence, ItemTemplateId = Elixir.Id, MaxStock = 5, RestockSeconds = 600,
            Costs = [new VendorStockCost { VendorStockId = 3, ItemTemplateId = Tonic.Id, Count = 2 }],
        },
        new() { Id = 4, CreatureTemplateId = Smith, Sequence = CharmSequence, ItemTemplateId = Charm.Id, PriceOverride = 40 },
        new()
        {
            Id = 5, CreatureTemplateId = Smith, Sequence = GatedSequence, ItemTemplateId = Plate.Id,
            RequiredQuestId = GatedQuest, RequiredQuestState = QuestRequirementState.Completed,
        },
        new() { Id = 10, CreatureTemplateId = Pedlar, Sequence = 1, ItemTemplateId = Tonic.Id },
    ];

    public static VendorCatalog Catalog(IReadOnlyCollection<VendorStock>? rows = null) =>
        new(rows ?? Rows(), Items, NullLoggerFactory.Instance);
}
