using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.World.Entities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Avalon.Server.World.UnitTests.Entities;

/// <summary>
/// The container used to log a count and discard what it was handed, so every read of an
/// inventory returned nothing and no test could tell. These hold it to keeping what it loads.
/// </summary>
public class CharacterInventoryContainerShould
{
    private static InventoryItem Item(ushort slot, ulong template = 1, uint count = 1) =>
        new(slot, new ItemInstanceId(Guid.NewGuid()), new ItemTemplateId(template),
            count, Durability: 100, ItemInstanceFlags.None);

    private static CharacterInventoryContainer Bag() =>
        new(NullLoggerFactory.Instance, InventoryType.Bag);

    [Fact]
    public void Return_What_It_Was_Loaded_With()
    {
        var container = Bag();

        container.Load([Item(0), Item(1), Item(2)]);

        Assert.Equal(3, container.Items.Count);
        Assert.Equal<ushort>([0, 1, 2], container.Items.Select(i => i.Slot).Order());
    }

    [Fact]
    public void Find_An_Item_By_Its_Slot()
    {
        var container = Bag();
        container.Load([Item(4, template: 77)]);

        Assert.True(container.TryGet(4, out InventoryItem found));
        Assert.Equal(new ItemTemplateId(77), found.TemplateId);

        Assert.False(container.TryGet(5, out _));
    }

    [Fact]
    public void Replace_Its_Whole_Contents_On_Reload()
    {
        var container = Bag();
        container.Load([Item(0), Item(1)]);

        container.Load([Item(9)]);

        Assert.Single(container.Items);
        Assert.False(container.TryGet(0, out _));
        Assert.True(container.TryGet(9, out _));
    }

    /// <summary>
    /// Equipment declares 14 slots. A row past the end is bad data, and sizing the container to
    /// it would let one row decide how big every inventory is.
    /// </summary>
    [Fact]
    public void Refuse_A_Slot_Beyond_Its_Capacity()
    {
        var equipment = new CharacterInventoryContainer(NullLoggerFactory.Instance, InventoryType.Equipment);

        equipment.Load([Item(0), Item(14), Item(13)]);

        Assert.Equal(2, equipment.Items.Count);
        Assert.False(equipment.TryGet(14, out _));
    }

    [Fact]
    public void Start_Empty()
    {
        Assert.Empty(Bag().Items);
    }
}
