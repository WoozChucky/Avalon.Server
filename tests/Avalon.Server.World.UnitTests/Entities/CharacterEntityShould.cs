using System.Reflection;
using Avalon.Combat;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.State;
using Avalon.World.Entities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

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
    /// #977: the entity keeps the instance id it set instead of parsing the row on every read, but the row is what is
    /// saved, so the row always holds the id, and a row written another way (a load, a direct write) is what a read sees.
    /// </summary>
    [Fact]
    public void Keep_the_instance_id_and_the_row_that_is_saved_in_step()
    {
        CharacterEntity entity = NewEntity();
        Assert.Equal(Guid.Empty, entity.InstanceId);

        var first = Guid.NewGuid();
        entity.InstanceId = first;
        Assert.Equal(first, entity.InstanceId);
        Assert.Equal(first.ToString(), entity.Data!.InstanceId);

        string written = entity.Data.InstanceId!;
        entity.InstanceId = first;
        Assert.Same(written, entity.Data.InstanceId); // unchanged: nothing formatted, the row keeps its text

        var direct = Guid.NewGuid();
        entity.Data.InstanceId = direct.ToString();
        Assert.Equal(direct, entity.InstanceId);

        entity.InstanceId = first;
        Assert.Equal(first, entity.InstanceId);
        Assert.Equal(first.ToString(), entity.Data.InstanceId);

        entity.Data.InstanceId = null;
        Assert.Equal(Guid.Empty, entity.InstanceId);
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

        entity.ApplyStats(new Avalon.Combat.DerivedCharacterStats(
            MaxHealth: uint.MaxValue, MaxPower: (uint)int.MaxValue + 1, Stamina: 1, Strength: 1, Agility: 1,
            Intellect: 1, Armor: 0, BlockPct: 0, Evasion: 0, CritPct: 0),
            Avalon.World.Characters.CurrentValues.Refill, TestCombat.Formula);

        Assert.Equal(int.MaxValue, entity.Data!.Health);
        Assert.Equal(int.MaxValue, entity.Data.Power1);
        Assert.Equal((uint)int.MaxValue, entity.Health);
        Assert.Equal((uint)int.MaxValue, entity.Power);
        Assert.Equal((uint)int.MaxValue, entity.CurrentHealth);
        Assert.Equal((uint)int.MaxValue, entity.CurrentPower);
    }

    /// <summary>
    /// #424. Input from a dead character is dropped, so nothing else would ever clear the velocity it
    /// had when it died, and other clients would extrapolate the corpse onwards.
    /// </summary>
    [Fact]
    public void Come_to_rest_when_it_dies()
    {
        CharacterEntity entity = NewEntity();
        entity.Velocity = new Avalon.Common.Mathematics.Vector3(5f, 0f, 0f);
        entity.ConsumeDirtyFields();

        entity.IsDead = true;

        Assert.Equal(Avalon.Common.Mathematics.Vector3.zero, entity.Velocity);
        Assert.True((entity.ConsumeDirtyFields() & GameEntityFields.Velocity) != 0);
    }

    [Fact]
    public void Not_dirty_when_IsDead_setter_value_unchanged()
    {
        CharacterEntity entity = NewEntity();
        entity.IsDead = true;
        entity.ConsumeDirtyFields();

        entity.IsDead = true;

        GameEntityFields dirty = entity.ConsumeDirtyFields();
        Assert.True((dirty & GameEntityFields.IsDead) == 0);
    }

    [Fact]
    public void Revive_clears_IsDead_and_restores_full_health()
    {
        CharacterEntity entity = NewEntity();
        entity.IsDead = true;
        // Poke _currentHealth to 0 directly to simulate post-death state.
        FieldInfo? hpField = typeof(CharacterEntity).GetField("_currentHealth",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        hpField!.SetValue(entity, 0u);
        entity.ConsumeDirtyFields();

        entity.Revive();

        Assert.False(entity.IsDead);
        Assert.Equal(entity.Health, entity.CurrentHealth);
        GameEntityFields dirty = entity.ConsumeDirtyFields();
        Assert.True((dirty & GameEntityFields.IsDead) != 0);
        Assert.True((dirty & GameEntityFields.CurrentHealth) != 0);
    }

    [Fact]
    public void Set_IsDead_and_clamp_health_to_zero_when_OnHit_takes_HP_below_zero()
    {
        CharacterEntity entity = NewEntity();
        IUnit attacker = Substitute.For<Avalon.World.Public.Units.IUnit>();

        entity.OnHit(attacker, damage: 9999);

        Assert.True(entity.IsDead);
        Assert.Equal(0u, entity.CurrentHealth);
    }
}
