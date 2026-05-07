using Avalon.World.Public.Abilities;
using Xunit;

namespace Avalon.Server.World.UnitTests.Abilities;

public class AbilityFlagsShould
{
    [Fact]
    public void Should_combine_flags_via_bitwise_or()
    {
        AbilityFlags combined = AbilityFlags.RequiresOutOfCombat | AbilityFlags.RequiresInCombat;
        Assert.True(combined.HasFlag(AbilityFlags.RequiresOutOfCombat));
        Assert.True(combined.HasFlag(AbilityFlags.RequiresInCombat));
    }

    [Fact]
    public void Should_default_to_None()
    {
        AbilityFlags flags = default;
        Assert.Equal(AbilityFlags.None, flags);
    }
}
