using Avalon.Domain.World;
using Avalon.World.Inventory;
using Avalon.World.Public.Enums;

namespace Avalon.Server.World.UnitTests.Inventory;

/// <summary>The spec's equipment table, which the client team builds its paper doll from.</summary>
public class EquipmentSlotsShould
{
    [Theory]
    [InlineData(0, ItemSlotType.Head)]
    [InlineData(1, ItemSlotType.Neck)]
    [InlineData(2, ItemSlotType.Shoulder)]
    [InlineData(3, ItemSlotType.Chest)]
    [InlineData(4, ItemSlotType.Hands)]
    [InlineData(5, ItemSlotType.Legs)]
    [InlineData(6, ItemSlotType.Feet)]
    [InlineData(7, ItemSlotType.Finger)]
    [InlineData(8, ItemSlotType.Finger)]
    [InlineData(9, ItemSlotType.MainHand)]
    [InlineData(10, ItemSlotType.OffHand)]
    public void Map_Each_Slot_To_The_Item_Slot_Type_It_Holds(ushort slot, ItemSlotType type)
    {
        Assert.Equal(type, EquipmentSlots.TypeOf(slot));
        Assert.True(EquipmentSlots.Accepts(slot, type));
        Assert.False(EquipmentSlots.IsReserved(slot));
    }

    [Theory]
    [InlineData(0u, 5u, true)]
    [InlineData(2u, 29u, true)]
    [InlineData(3u, 0u, false)]
    [InlineData(1u, 70000u, false)]
    public void Read_A_Slot_Off_The_Wire_Only_When_It_Can_Name_One(uint container, uint slot, bool readable)
    {
        Assert.Equal(readable, SlotRef.TryParse(container, slot, out SlotRef parsed));
        if (readable)
            Assert.Equal(new SlotRef((InventoryType)container, (ushort)slot), parsed);
    }
}
