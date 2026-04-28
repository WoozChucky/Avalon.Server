using Avalon.Domain.Characters;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Entities;

public class CharacterEntityShould
{
    private static CharacterEntity NewEntity()
    {
        var character = new Character
        {
            Id = 1u,
            Health = 100,
            Power1 = 0,
        };
        return new CharacterEntity(NullLoggerFactory.Instance, character, new RegenConfiguration());
    }

    [Fact]
    public void Mark_IsDead_dirty_when_setter_flips_true()
    {
        var entity = NewEntity();
        // Drain any default dirty bits first.
        entity.ConsumeDirtyFields();

        entity.IsDead = true;

        var dirty = entity.ConsumeDirtyFields();
        Assert.True((dirty & GameEntityFields.IsDead) != 0);
        Assert.True(entity.IsDead);
    }

    [Fact]
    public void Not_dirty_when_IsDead_setter_value_unchanged()
    {
        var entity = NewEntity();
        entity.IsDead = true;
        entity.ConsumeDirtyFields();

        entity.IsDead = true;

        var dirty = entity.ConsumeDirtyFields();
        Assert.True((dirty & GameEntityFields.IsDead) == 0);
    }

    [Fact]
    public void Revive_clears_IsDead_and_restores_full_health()
    {
        var entity = NewEntity();
        entity.IsDead = true;
        // Poke _currentHealth to 0 directly to simulate post-death state.
        var hpField = typeof(CharacterEntity).GetField("_currentHealth",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        hpField!.SetValue(entity, 0u);
        entity.ConsumeDirtyFields();

        entity.Revive();

        Assert.False(entity.IsDead);
        Assert.Equal(entity.Health, entity.CurrentHealth);
        var dirty = entity.ConsumeDirtyFields();
        Assert.True((dirty & GameEntityFields.IsDead) != 0);
        Assert.True((dirty & GameEntityFields.CurrentHealth) != 0);
    }

    [Fact]
    public void Set_IsDead_and_clamp_health_to_zero_when_OnHit_takes_HP_below_zero()
    {
        var entity = NewEntity();
        var attacker = Substitute.For<Avalon.World.Public.Units.IUnit>();

        entity.OnHit(attacker, damage: 9999);

        Assert.True(entity.IsDead);
        Assert.Equal(0u, entity.CurrentHealth);
    }

    [Fact]
    public void Ignore_subsequent_OnHit_while_dead()
    {
        var entity = NewEntity();
        var attacker = Substitute.For<Avalon.World.Public.Units.IUnit>();
        entity.OnHit(attacker, damage: 9999);
        Assert.True(entity.IsDead);

        bool damagedRaised = false;
        UnitDamagedDelegate handler = (_, _, _) => damagedRaised = true;
        CharacterEntity.OnUnitDamaged += handler;

        try
        {
            entity.OnHit(attacker, damage: 5);

            Assert.False(damagedRaised);
            Assert.Equal(0u, entity.CurrentHealth);
            Assert.True(entity.IsDead);
        }
        finally
        {
            CharacterEntity.OnUnitDamaged -= handler;
        }
    }
}
