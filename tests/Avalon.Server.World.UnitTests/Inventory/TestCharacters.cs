using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public.Characters;
using Avalon.World.Reload;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.Inventory;

/// <summary>A real CharacterEntity and a fixed template catalogue for the economy tests.</summary>
internal static class TestCharacters
{
    public static readonly ItemTemplate Potion = new()
    { Id = new ItemTemplateId(100), Name = "Potion", Class = ItemClass.Consumable, MaxStackSize = 20 };
    public static readonly ItemTemplate Sword = new()
    { Id = new ItemTemplateId(200), Name = "Sword", Class = ItemClass.Weapon, MaxStackSize = 1 };
    public static readonly ItemTemplate Relic = new()
    { Id = new ItemTemplateId(300), Name = "Relic", Class = ItemClass.Quest, MaxStackSize = 1, Flags = ItemTemplateFlags.Unique };
    public static readonly ItemTemplate Pebble = new()
    { Id = new ItemTemplateId(400), Name = "Pebble", Class = ItemClass.Junk, MaxStackSize = 0 };
    /// <summary>The widest stack a template can declare: room in a bag of these is past what a uint holds.</summary>
    public static readonly ItemTemplate Hoard = new()
    { Id = new ItemTemplateId(500), Name = "Hoard", Class = ItemClass.Junk, MaxStackSize = uint.MaxValue };

    private static readonly Dictionary<ItemTemplateId, ItemTemplate> s_templates =
        new[] { Potion, Sword, Relic, Pebble, Hoard }.ToDictionary(t => t.Id);

    public static ItemTemplate? Find(ItemTemplateId id) => s_templates.GetValueOrDefault(id);

    public static CharacterEntity New(uint id = 7, ulong gold = 0, TimeProvider? time = null)
    {
        var row = new Character
        {
            Id = new CharacterId(id),
            AccountId = new AccountId(1),
            Name = NameFor(id),
            Class = CharacterClass.Warrior,
            Gold = gold,
            CreationDate = (time ?? TimeProvider.System).GetUtcNow().UtcDateTime,
        };
        return new CharacterEntity(NullLoggerFactory.Instance, row, new RegenConfiguration(), time) { Data = row };
    }

    /// <summary>
    /// The name <see cref="New" /> gives character <paramref name="id" />: "Tester" and the id's digits as letters
    /// (7 is "Testerh"), so it follows the name rule the database holds every row to (#944).
    /// </summary>
    public static string NameFor(uint id) =>
        "Tester" + string.Concat(id.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(d => (char)(d - '0' + 'a')));

    public static InventoryItem Item(byte slot, ItemTemplate template, uint count = 1, uint durability = 0, uint charges = 0,
        bool identified = true) =>
        new(slot, new ItemInstanceId(Guid.CreateVersion7()), template.Id, count, durability, ItemInstanceFlags.None, template.Rarity, 1,
            RareName: null, identified, charges);

    /// <summary>The item outside a container with no rolled rows, as a sale or a trade carries it (#1179).</summary>
    public static CarriedItem Carried(InventoryItem item) => new(item, default, []);

    /// <summary>
    /// An inventory over this catalogue, or over <paramref name="find" /> when given one, and over the modifier catalog
    /// <paramref name="modifiers" /> when given one (#1179), else an empty one: a copy it adds rolls its template's base-stat
    /// ranges, and modifiers only from that catalog.
    /// </summary>
    public static CharacterInventoryService InventoryFor(
        CharacterEntity character, Func<ItemTemplateId, ItemTemplate?>? find = null, IItemIdAllocator? ids = null,
        ItemModifierCatalog? modifiers = null)
    {
        ids ??= new ItemIdAllocator();
        var items = new ItemsPatch([], modifiers ?? ItemModifierRepositories.EmptyCatalog());
        return new CharacterInventoryService(character, find ?? Find, ids, TestEconomy.Roller(ids), () => items);
    }

    public static InventoryItem At(CharacterEntity character, InventoryType container, byte slot)
    {
        Assert.True(character.Container(container).TryGet(slot, out InventoryItem item), $"{container} slot {slot} is empty");
        return item;
    }
}
