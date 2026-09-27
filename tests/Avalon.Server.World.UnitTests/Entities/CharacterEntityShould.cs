using Avalon.Domain.Characters;
using Avalon.Network.Packets.State;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Public.Abilities;
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
        var entity = new CharacterEntity(NullLoggerFactory.Instance, character, new RegenConfiguration());
        // Initialize spells with an empty collection to avoid NullReferenceException in Update
        entity.Spells.Load(new List<IAbility>());
        return entity;
    }

    /// <summary>
    /// #506: the row stores the maximums as int, so a gear total past int.MaxValue is clamped there, not
    /// cast to a negative, and a pool filled to the new maximum is filled to what was stored.
    /// </summary>
    [Fact]
    public void Store_a_maximum_past_int_max_as_int_max_and_fill_to_it()
    {
        CharacterEntity entity = NewEntity();
        entity.PowerType = PowerType.Mana;

        entity.ApplyStats(new Avalon.World.Characters.DerivedCharacterStats(
            MaxHealth: uint.MaxValue, MaxPower: (uint)int.MaxValue + 1, Stamina: 1, Strength: 1, Agility: 1,
            Intellect: 1, Armor: 0, BlockPct: 0, DodgePct: 0, CritPct: 0, AttackDamage: 0, AbilityDamage: 0),
            Avalon.World.Characters.CurrentValues.Refill);

        Assert.Equal(int.MaxValue, entity.Data!.Health);
        Assert.Equal(int.MaxValue, entity.Data.Power1);
        Assert.Equal((uint)int.MaxValue, entity.Health);
        Assert.Equal((uint)int.MaxValue, entity.Power);
        Assert.Equal((uint)int.MaxValue, entity.CurrentHealth);
        Assert.Equal((uint)int.MaxValue, entity.CurrentPower);
    }

    /// <summary>
    /// The combat tag lasts by the entity's clock, the instance's (#614), not the wall clock: the
    /// clock sits years from now, and only it moves.
    /// </summary>
    [Fact]
    public void Stay_in_combat_by_its_clock_until_the_leave_delay_has_passed()
    {
        var clock = new Avalon.Server.World.UnitTests.Loot.FixedTimeProvider(new DateTimeOffset(2001, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var config = new RegenConfiguration();
        var entity = new CharacterEntity(NullLoggerFactory.Instance, new Character { Id = 1u, Health = 100 }, config, clock);

        entity.MarkCombat();
        Assert.True(entity.IsInCombat);

        clock.Now = clock.Now.AddSeconds(config.CombatLeaveDelaySeconds);
        Assert.False(entity.IsInCombat);
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

    /// <summary>
    /// #424. Input from a dead character is dropped, so nothing else would ever clear the velocity it
    /// had when it died, and other clients would extrapolate the corpse onwards.
    /// </summary>
    [Fact]
    public void Come_to_rest_when_it_dies()
    {
        var entity = NewEntity();
        entity.Velocity = new Avalon.Common.Mathematics.Vector3(5f, 0f, 0f);
        entity.ConsumeDirtyFields();

        entity.IsDead = true;

        Assert.Equal(Avalon.Common.Mathematics.Vector3.zero, entity.Velocity);
        Assert.True((entity.ConsumeDirtyFields() & GameEntityFields.Velocity) != 0);
    }

    [Fact]
    public void Stay_at_rest_after_it_is_revived()
    {
        var entity = NewEntity();
        entity.Velocity = new Avalon.Common.Mathematics.Vector3(5f, 0f, 0f);
        entity.IsDead = true;

        entity.Revive();

        Assert.Equal(Avalon.Common.Mathematics.Vector3.zero, entity.Velocity);
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

        // What a hit on a corpse sends (nothing) is the combat service's to decide (#546);
        // CombatServiceShould pins it.
        entity.OnHit(attacker, damage: 5);

        Assert.Equal(0u, entity.CurrentHealth);
        Assert.True(entity.IsDead);
    }

    [Fact]
    public void Regen_does_not_increase_health_while_dead()
    {
        var entity = NewEntity();
        var attacker = Substitute.For<Avalon.World.Public.Units.IUnit>();
        entity.OnHit(attacker, damage: 9999);
        Assert.True(entity.IsDead);
        Assert.Equal(0u, entity.CurrentHealth);

        // Run several update ticks. Even outside-combat regen should not move CurrentHealth.
        for (int i = 0; i < 60; i++)
        {
            entity.Update(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(0u, entity.CurrentHealth);
    }
}
