using Avalon.Domain.World;

namespace Avalon.Server.World.UnitTests.Entities;

/// <summary>
/// An item's attribute requirements (#1178, #1179): every one it sets must be met, or, for a base that asks either of
/// two attributes (the mace's Strength or Intellect), at least one; an item that sets none is always met.
/// </summary>
public class ItemTemplateShould
{
    [Theory]
    [InlineData(30u, 30u, false, 30u, 0u, 30u, true)]
    [InlineData(30u, 30u, false, 31u, 0u, 29u, false)]
    [InlineData(30u, 30u, true, 31u, 0u, 20u, true)]
    [InlineData(30u, 30u, true, 20u, 0u, 31u, true)]
    [InlineData(30u, 30u, true, 29u, 0u, 29u, false)]
    [InlineData(null, null, true, 0u, 0u, 0u, true)]
    public void Meet_attribute_requirements_all_or_any(
        uint? requiredStrength, uint? requiredIntellect, bool any, uint str, uint agi, uint intl, bool meets)
    {
        var item = new ItemTemplate
        {
            RequiredStrength = (ushort?)requiredStrength,
            RequiredIntellect = (ushort?)requiredIntellect,
            RequiresAnyAttribute = any,
        };

        Assert.Equal(meets, item.MeetsAttributes(str, agi, intl));
    }
}
