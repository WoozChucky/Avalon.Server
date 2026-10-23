using Avalon.Domain.World;

namespace Avalon.Server.World.UnitTests.Entities;

/// <summary>
/// An item's attribute requirements (#1178, #1179): every one it sets must be met, or, for a base that asks either of
/// two attributes (the mace's Strength or Intellect), at least one; an item that sets none is always met.
/// </summary>
public class ItemTemplateShould
{
    [Theory]
    [InlineData(false, 30u, 0u, 30u, true)]
    [InlineData(false, 31u, 0u, 29u, false)]
    [InlineData(true, 31u, 0u, 20u, true)]
    [InlineData(true, 20u, 0u, 31u, true)]
    [InlineData(true, 29u, 0u, 29u, false)]
    public void Meet_attribute_requirements_all_or_any(bool any, uint str, uint agi, uint intl, bool meets)
    {
        var mace = new ItemTemplate { RequiredStrength = 30, RequiredIntellect = 30, RequiresAnyAttribute = any };

        Assert.Equal(meets, mace.MeetsAttributes(str, agi, intl));
        Assert.True(new ItemTemplate { RequiresAnyAttribute = true }.MeetsAttributes(0, 0, 0));
    }
}
