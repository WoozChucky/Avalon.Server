using Avalon.Common;
using Avalon.World.Entities;
using Avalon.World.Public.Enums;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;
using Xunit;

namespace Avalon.Server.World.UnitTests.Entities;

public class EntityDirtyFlagShould
{
    // ──────────────────────────────────────────────
    // Creature
    // ──────────────────────────────────────────────

    [Fact]
    public void Creature_Should_MarkCurrentHealth_Dirty_WhenSet()
    {
        var c = new Creature();
        c.ConsumeDirtyFields(); // clear construction noise

        c.CurrentHealth = 80u;

        var dirty = c.ConsumeDirtyFields();
        Assert.True(dirty.HasFlag(GameEntityFields.CurrentHealth));
    }

    [Fact]
    public void Creature_Should_MarkPosition_Dirty_WhenSet()
    {
        var c = new Creature();
        c.ConsumeDirtyFields();

        c.Position = new Vector3(1, 2, 3);

        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.Position));
    }

    [Fact]
    public void Creature_Should_MarkVelocity_Dirty_WhenSet()
    {
        var c = new Creature();
        c.ConsumeDirtyFields();
        c.Velocity = new Vector3(0, 1, 0);
        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.Velocity));
    }

    [Fact]
    public void Creature_Should_MarkOrientation_Dirty_WhenSet()
    {
        var c = new Creature();
        c.ConsumeDirtyFields();
        c.Orientation = new Vector3(0, 90, 0);
        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.Orientation));
    }

    [Fact]
    public void Creature_Should_MarkMoveState_Dirty_WhenSet()
    {
        var c = new Creature();
        c.ConsumeDirtyFields();
        c.MoveState = MoveState.Running;
        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.MoveState));
    }

    [Fact]
    public void Creature_Should_MarkHealth_Dirty_WhenSet()
    {
        var c = new Creature();
        c.ConsumeDirtyFields();
        c.Health = 200u;
        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.Health));
    }

    [Fact]
    public void Creature_Should_MarkLevel_Dirty_WhenSet()
    {
        var c = new Creature();
        c.ConsumeDirtyFields();
        c.Level = 5;
        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.Level));
    }

    [Fact]
    public void Creature_Should_MarkCurrentPower_Dirty_WhenSet()
    {
        var c = new Creature();
        c.ConsumeDirtyFields();
        c.CurrentPower = 50u;
        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.CurrentPower));
    }

    [Fact]
    public void Creature_Should_MarkPower_Dirty_WhenSet()
    {
        var c = new Creature();
        c.ConsumeDirtyFields();
        c.Power = 100u;
        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.Power));
    }

    [Fact]
    public void Creature_Should_MarkPowerType_Dirty_WhenSet()
    {
        var c = new Creature();
        c.ConsumeDirtyFields();
        c.PowerType = PowerType.Mana;
        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.PowerType));
    }

    [Fact]
    public void Creature_Should_AccumulateMultipleDirtyFields_BeforeConsume()
    {
        var c = new Creature();
        c.ConsumeDirtyFields();

        c.CurrentHealth = 50u;
        c.Position = new Vector3(5, 0, 5);

        var dirty = c.ConsumeDirtyFields();
        Assert.True(dirty.HasFlag(GameEntityFields.CurrentHealth));
        Assert.True(dirty.HasFlag(GameEntityFields.Position));
    }

    [Fact]
    public void Creature_Should_ReturnNone_OnSecondConsume_WithoutMutation()
    {
        var c = new Creature();
        c.ConsumeDirtyFields();
        c.CurrentHealth = 70u;
        c.ConsumeDirtyFields(); // first consume clears

        var second = c.ConsumeDirtyFields();
        Assert.False(second.HasFlag(GameEntityFields.CurrentHealth));
    }

    // ──────────────────────────────────────────────
    // CharacterEntity
    // ──────────────────────────────────────────────

    [Fact]
    public void Character_Should_MarkCurrentHealth_Dirty_WhenSet()
    {
        var c = new CharacterEntity();
        c.ConsumeDirtyFields();
        c.CurrentHealth = 90u;
        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.CurrentHealth));
    }

    [Fact]
    public void Character_Should_MarkCurrentPower_Dirty_WhenSet()
    {
        var c = new CharacterEntity();
        c.ConsumeDirtyFields();
        c.CurrentPower = 40u;
        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.CurrentPower));
    }

    [Fact]
    public void Character_Should_MarkVelocity_Dirty_WhenSet()
    {
        var c = new CharacterEntity();
        c.ConsumeDirtyFields();
        c.Velocity = new Vector3(1, 0, 0);
        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.Velocity));
    }

    [Fact]
    public void Character_Should_MarkMoveState_Dirty_WhenSet()
    {
        var c = new CharacterEntity();
        c.ConsumeDirtyFields();
        c.MoveState = MoveState.Running;
        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.MoveState));
    }

    [Fact]
    public void Character_Should_MarkRequiredExperience_Dirty_WhenSet()
    {
        var c = new CharacterEntity();
        c.ConsumeDirtyFields();
        c.RequiredExperience = 5000ul;
        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.RequiredExperience));
    }

    [Fact]
    public void Character_Should_MarkPowerType_Dirty_WhenSet()
    {
        var c = new CharacterEntity();
        c.ConsumeDirtyFields();
        c.PowerType = PowerType.Mana;
        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.PowerType));
    }

    [Fact]
    public void Character_Should_AccumulateMultipleDirtyFields_BeforeConsume()
    {
        var c = new CharacterEntity();
        c.ConsumeDirtyFields();
        c.CurrentHealth = 50u;
        c.MoveState = MoveState.Running;
        var dirty = c.ConsumeDirtyFields();
        Assert.True(dirty.HasFlag(GameEntityFields.CurrentHealth));
        Assert.True(dirty.HasFlag(GameEntityFields.MoveState));
    }

    [Fact]
    public void Character_Should_ReturnNone_OnSecondConsume_WithoutMutation()
    {
        var c = new CharacterEntity();
        c.ConsumeDirtyFields();
        c.CurrentHealth = 60u;
        c.ConsumeDirtyFields();
        Assert.False(c.ConsumeDirtyFields().HasFlag(GameEntityFields.CurrentHealth));
    }
}
