using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.World.Inventory;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Avalon.Server.World.UnitTests.Entities;

/// <summary>
/// The row says where an item sits; the instance says what it is. Both live in the Character
/// database but are read as two queries, so this correlation is what puts them together -- and a
/// row whose instance is gone must not become a slot holding template zero, which a client would
/// render as something rather than as nothing.
/// </summary>
public class InventoryAssemblerShould
{
    private static readonly CharacterId Owner = new(1);

    private static CharacterInventory Row(InventoryType container, ushort slot, Guid itemId) =>
        new() { CharacterId = Owner, Container = container, Slot = slot, ItemId = new ItemInstanceId(itemId) };

    private static ItemInstance Instance(Guid id, ulong template = 5, uint count = 1, uint durability = 50) =>
        new()
        {
            Id = new ItemInstanceId(id),
            TemplateId = new ItemTemplateId(template),
            CharacterId = Owner,
            Count = count,
            Durability = durability,
            Flags = ItemInstanceFlags.None,
        };

    private static IReadOnlyDictionary<InventoryType, List<Avalon.World.Public.Characters.InventoryItem>>
        Assemble(IReadOnlyCollection<CharacterInventory> rows, IReadOnlyCollection<ItemInstance> instances)
        => InventoryAssembler.Assemble(rows, instances, NullLogger.Instance);

    [Fact]
    public void Group_Items_By_Their_Container()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();

        var result = Assemble(
            [Row(InventoryType.Equipment, 0, a), Row(InventoryType.Bag, 1, b), Row(InventoryType.Bank, 2, c)],
            [Instance(a), Instance(b), Instance(c)]);

        Assert.Single(result[InventoryType.Equipment]);
        Assert.Single(result[InventoryType.Bag]);
        Assert.Single(result[InventoryType.Bank]);
    }

    [Fact]
    public void Carry_The_Instance_Data_Onto_The_Item()
    {
        Guid id = Guid.NewGuid();

        var result = Assemble(
            [Row(InventoryType.Bag, 3, id)],
            [Instance(id, template: 4242, count: 7, durability: 33)]);

        var item = Assert.Single(result[InventoryType.Bag]);
        Assert.Equal((ushort)3, item.Slot);
        Assert.Equal(new ItemTemplateId(4242), item.TemplateId);
        Assert.Equal(new ItemInstanceId(id), item.InstanceId);
        Assert.Equal(7u, item.Count);
        Assert.Equal(33u, item.Durability);
    }

    /// <summary>
    /// Review Focus 1. Two rows in one slot is bad data; the slot must not end up holding both.
    /// </summary>
    [Fact]
    public void Keep_Only_One_Row_When_Two_Claim_The_Same_Slot()
    {
        Guid first = Guid.NewGuid(), second = Guid.NewGuid();

        var result = Assemble(
            [Row(InventoryType.Bag, 2, first), Row(InventoryType.Bag, 2, second)],
            [Instance(first, template: 11), Instance(second, template: 22)]);

        var item = Assert.Single(result[InventoryType.Bag]);
        Assert.Equal(new ItemTemplateId(11), item.TemplateId);
    }

    /// <summary>
    /// Review Focus 2. A stack that reached zero without its row being deleted is a data bug --
    /// hiding it here would make that bug invisible rather than fix it.
    /// </summary>
    [Fact]
    public void Keep_An_Item_Whose_Count_Is_Zero()
    {
        Guid id = Guid.NewGuid();

        var result = Assemble([Row(InventoryType.Bag, 0, id)], [Instance(id, count: 0)]);

        Assert.Equal(0u, Assert.Single(result[InventoryType.Bag]).Count);
    }

    /// <summary>
    /// Review Focus 3. A container value outside the enum must not reach a container lookup.
    /// </summary>
    [Fact]
    public void Skip_A_Row_Whose_Container_Is_Not_A_Known_One()
    {
        Guid id = Guid.NewGuid();
        var row = Row(InventoryType.Bag, 0, id);
        row.Container = (InventoryType)999;

        var result = Assemble([row], [Instance(id)]);

        Assert.All(result.Values, list => Assert.Empty(list));
    }

    [Fact]
    public void Skip_A_Row_Whose_Instance_Is_Missing()
    {
        Guid present = Guid.NewGuid(), orphan = Guid.NewGuid();

        var result = Assemble(
            [Row(InventoryType.Bag, 0, orphan), Row(InventoryType.Bag, 1, present)],
            [Instance(present, template: 9)]);

        var item = Assert.Single(result[InventoryType.Bag]);
        Assert.Equal(new ItemTemplateId(9), item.TemplateId);
    }

    [Fact]
    public void Return_A_List_For_Every_Container_When_There_Is_Nothing()
    {
        var result = Assemble([], []);

        Assert.Empty(result[InventoryType.Equipment]);
        Assert.Empty(result[InventoryType.Bag]);
        Assert.Empty(result[InventoryType.Bank]);
    }

    /// <summary>A save writes the whole instance back, so a charge the assembler dropped would be lost.</summary>
    [Fact]
    public void Carry_the_charges_onto_the_item()
    {
        Guid id = Guid.NewGuid();
        ItemInstance instance = Instance(id);
        instance.Charges = 6;

        var result = Assemble([Row(InventoryType.Bag, 0, id)], [instance]);

        Assert.Equal(6u, Assert.Single(result[InventoryType.Bag]).Charges);
    }
}
