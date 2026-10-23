using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;
using Avalon.Server.World.UnitTests.Combat;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;
using Avalon.World.Serialization;
using Microsoft.Extensions.Time.Testing;

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

    /// <summary>
    /// #1178: a hit the ward takes marks the ward, and the state written for it carries the ward with its maximum, so a
    /// client always holds the pair its ward bar is drawn from.
    /// </summary>
    [Fact]
    public void Replicate_a_units_ward_and_its_maximum_when_they_change()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero));
        var combat = new CombatService(new CombatConfig(), new EncounterRegistry(new CombatConfig(), time), time: time,
            random: ScriptedCombatRandom.Plain());
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 1178_902),
            Metadata = Loot.LootTestData.BoarTemplate(null),
            Level = 1,
            Health = 100,
            CurrentHealth = 100,
            BaseWard = 30,
        };
        creature.ConsumeDirtyFields();

        combat.ApplyDamage(TestCharacters.New(1178_102, time: time), creature, 10);

        GameEntityFields fields = creature.ConsumeDirtyFields() & GameEntityFields.CreatureUpdate;
        Assert.True(fields.HasFlag(GameEntityFields.Ward));
        ObjectState state = ObjectStateWriter.From(creature, fields);
        Assert.Equal((20u, 30u), (state.Ward, state.MaxWard));
    }
}
