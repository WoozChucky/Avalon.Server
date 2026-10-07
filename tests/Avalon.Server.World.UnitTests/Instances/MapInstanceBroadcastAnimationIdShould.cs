using Avalon.World.Instances;
using Avalon.World.Public.Abilities;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Instances;

public class MapInstanceBroadcastAnimationIdShould
{
    /// <summary>
    /// The swing broadcast carries its ability's animation id, 0 included (the client reads 0 as "no
    /// animation", so it must not fall back), and 1, the old melee swing's, when no ability backs the swing.
    /// </summary>
    [Theory]
    [InlineData(5u, (ushort)5)]
    [InlineData(0u, (ushort)0)]
    [InlineData(null, (ushort)1)]
    public void Carry_the_abilitys_animation_id_or_1_without_one(uint? animationId, ushort expected)
    {
        IAbility? ability = null;
        if (animationId is not null)
        {
            ability = Substitute.For<IAbility>();
            ability.Metadata.Returns(new AbilityMetadata { AnimationId = animationId.Value });
        }

        Assert.Equal(expected, MapInstance.ResolveBroadcastAnimationId(ability));
    }
}
