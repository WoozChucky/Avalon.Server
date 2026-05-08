using Avalon.Common.ValueObjects;
using Avalon.World.Instances;
using Avalon.World.Public.Abilities;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Instances;

public class MapInstanceBroadcastAnimationIdShould
{
    [Fact]
    public void Return_metadata_animation_id_when_ability_present()
    {
        var ability = Substitute.For<IAbility>();
        ability.Metadata.Returns(new AbilityMetadata { AnimationId = 5u });

        ushort result = MapInstance.ResolveBroadcastAnimationId(ability);

        Assert.Equal((ushort)5, result);
    }

    [Fact]
    public void Return_zero_when_metadata_animation_id_is_zero()
    {
        // Basic-attack ability templates seed AnimationId=0 on purpose; the broadcast must
        // honour that (client treats 0 as "no animation"), not silently fall back to 1.
        var ability = Substitute.For<IAbility>();
        ability.Metadata.Returns(new AbilityMetadata { AnimationId = 0u });

        ushort result = MapInstance.ResolveBroadcastAnimationId(ability);

        Assert.Equal((ushort)0, result);
    }

    [Fact]
    public void Fall_back_to_one_when_ability_is_null()
    {
        // Null ability == legacy melee auto-attack swing (no ability backing). The historic
        // hardcoded broadcast value was 1; preserve that as the fallback.
        ushort result = MapInstance.ResolveBroadcastAnimationId(null);

        Assert.Equal((ushort)1, result);
    }
}
