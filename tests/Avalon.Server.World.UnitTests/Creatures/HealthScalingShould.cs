using Avalon.World.Entities;

namespace Avalon.Server.World.UnitTests.Creatures;

/// <summary>
/// The edges of a party instance's health rescale (PartyHealthScalingShould drives the rescale itself through the
/// instance): a living creature is never rounded to 0 health, a corpse is never rescaled (#672), and a creature with
/// no base maximum is left alone.
/// </summary>
public class HealthScalingShould
{
    [Theory]
    [InlineData(1u, 1u, 1u, 0.001, 1u, 1u)]        // never rounded down to 0
    [InlineData(100u, 100u, 0u, 2.2, 100u, 0u)]    // a corpse
    [InlineData(0u, 80u, 40u, 2.2, 80u, 40u)]      // no base to scale from
    public void Never_round_a_living_creature_to_zero_or_rescale_a_corpse_or_a_creature_without_a_base(uint baseMax,
        uint health, uint current, double factor, uint expectedHealth, uint expectedCurrent)
    {
        var creature = new Creature { BaseMaxHealth = baseMax, Health = health, CurrentHealth = current };

        creature.Rescale(factor);

        Assert.Equal((expectedHealth, expectedCurrent), (creature.Health, creature.CurrentHealth));
    }
}
