using Avalon.World.Entities;
using Xunit;

namespace Avalon.Server.World.UnitTests.Creatures;

public class HealthScalingShould
{
    private static Creature Living(uint baseMax, uint current) =>
        new() { BaseMaxHealth = baseMax, Health = baseMax, CurrentHealth = current };

    [Fact]
    public void Scale_the_maximum_from_the_base_and_keep_the_share()
    {
        Creature creature = Living(100, 50);

        creature.Rescale(2.2);

        Assert.Equal(220u, creature.Health);
        Assert.Equal(110u, creature.CurrentHealth);

        creature.Rescale(1.0);

        Assert.Equal(100u, creature.Health);
        Assert.Equal(50u, creature.CurrentHealth);
    }

    [Fact]
    public void Keep_a_full_creature_full()
    {
        Creature creature = Living(100, 100);
        creature.Rescale(1.6);
        Assert.Equal(160u, creature.CurrentHealth);
    }

    [Fact]
    public void Never_round_a_living_creature_to_zero()
    {
        Creature creature = Living(1, 1);

        creature.Rescale(0.001);

        Assert.Equal(1u, creature.Health);
        Assert.Equal(1u, creature.CurrentHealth);
    }

    [Fact]
    public void Never_rescale_a_corpse()
    {
        Creature creature = Living(100, 0);

        creature.Rescale(2.2);

        Assert.Equal(100u, creature.Health);
        Assert.Equal(0u, creature.CurrentHealth);
    }

    [Fact]
    public void Leave_a_creature_without_a_base_alone()
    {
        var creature = new Creature { Health = 80, CurrentHealth = 40 };

        creature.Rescale(2.2);

        Assert.Equal(80u, creature.Health);
    }
}
