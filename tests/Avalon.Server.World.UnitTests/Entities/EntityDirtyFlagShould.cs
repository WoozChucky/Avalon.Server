using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;
using Avalon.World.Entities;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;

namespace Avalon.Server.World.UnitTests.Entities;

public class EntityDirtyFlagShould
{
    /// <summary>
    /// Each setter marks the field it changes, so the next state broadcast carries it; a setter that marks
    /// nothing leaves every client with the old value.
    /// </summary>
    [Theory]
    [InlineData(ObjectType.Creature, GameEntityFields.CurrentHealth)]
    [InlineData(ObjectType.Creature, GameEntityFields.Position)]
    [InlineData(ObjectType.Creature, GameEntityFields.Velocity)]
    [InlineData(ObjectType.Creature, GameEntityFields.Orientation)]
    [InlineData(ObjectType.Creature, GameEntityFields.MoveState)]
    [InlineData(ObjectType.Creature, GameEntityFields.Health)]
    [InlineData(ObjectType.Creature, GameEntityFields.Level)]
    [InlineData(ObjectType.Creature, GameEntityFields.CurrentPower)]
    [InlineData(ObjectType.Creature, GameEntityFields.Power)]
    [InlineData(ObjectType.Creature, GameEntityFields.PowerType)]
    [InlineData(ObjectType.Character, GameEntityFields.CurrentHealth)]
    [InlineData(ObjectType.Character, GameEntityFields.CurrentPower)]
    [InlineData(ObjectType.Character, GameEntityFields.Velocity)]
    [InlineData(ObjectType.Character, GameEntityFields.MoveState)]
    [InlineData(ObjectType.Character, GameEntityFields.RequiredExperience)]
    [InlineData(ObjectType.Character, GameEntityFields.PowerType)]
    public void Mark_the_field_its_setter_changes(ObjectType entity, GameEntityFields field)
    {
        IUnit unit = entity == ObjectType.Creature ? new Creature() : new CharacterEntity();
        unit.ConsumeDirtyFields(); // clear construction noise

        Action set = field switch
        {
            GameEntityFields.CurrentHealth => () => unit.CurrentHealth = 80u,
            GameEntityFields.Position => () => unit.Position = new Vector3(1, 2, 3),
            GameEntityFields.Velocity => () => unit.Velocity = new Vector3(0, 1, 0),
            GameEntityFields.Orientation => () => unit.Orientation = new Vector3(0, 90, 0),
            GameEntityFields.MoveState => () => unit.MoveState = MoveState.Running,
            GameEntityFields.Health => () => unit.Health = 200u,
            GameEntityFields.Level => () => unit.Level = 5,
            GameEntityFields.CurrentPower => () => unit.CurrentPower = 50u,
            GameEntityFields.Power => () => unit.Power = 100u,
            GameEntityFields.PowerType => () => unit.PowerType = PowerType.Mana,
            GameEntityFields.RequiredExperience => () => ((CharacterEntity)unit).RequiredExperience = 5000ul,
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        set();

        Assert.True(unit.ConsumeDirtyFields().HasFlag(field));
    }

    [Fact]
    public void Creature_Should_AccumulateMultipleDirtyFields_BeforeConsume()
    {
        var c = new Creature();
        c.ConsumeDirtyFields();

        c.CurrentHealth = 50u;
        c.Position = new Vector3(5, 0, 5);

        GameEntityFields dirty = c.ConsumeDirtyFields();
        Assert.True(dirty.HasFlag(GameEntityFields.CurrentHealth));
        Assert.True(dirty.HasFlag(GameEntityFields.Position));
    }

    [Fact]
    public void Character_Should_AccumulateMultipleDirtyFields_BeforeConsume()
    {
        var c = new CharacterEntity();
        c.ConsumeDirtyFields();
        c.CurrentHealth = 50u;
        c.MoveState = MoveState.Running;
        GameEntityFields dirty = c.ConsumeDirtyFields();
        Assert.True(dirty.HasFlag(GameEntityFields.CurrentHealth));
        Assert.True(dirty.HasFlag(GameEntityFields.MoveState));
    }
}
