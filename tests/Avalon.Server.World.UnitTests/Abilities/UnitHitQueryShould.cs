using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Abilities.Targeting;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Abilities;

public class UnitHitQueryShould
{
    private readonly Dictionary<ObjectGuid, ICharacter> _characters = new();
    private readonly Dictionary<ObjectGuid, ICreature> _creatures = new();

    private UnitHitQuery Query => new(_characters, _creatures);

    private ICreature Creature(uint id, float x, float z, float body = 0.5f, uint health = 10)
    {
        var creature = Substitute.For<ICreature>();
        creature.Guid.Returns(new ObjectGuid(ObjectType.Creature, id));
        creature.Position.Returns(new Vector3(x, 0f, z));
        creature.BodyRadius.Returns(body);
        creature.CurrentHealth.Returns(health);
        _creatures[creature.Guid] = creature;
        return creature;
    }

    private ICharacter Character(uint id, float x, float z, bool dead = false)
    {
        var character = Substitute.For<ICharacter>();
        character.Guid.Returns(new ObjectGuid(ObjectType.Character, id));
        character.Position.Returns(new Vector3(x, 0f, z));
        character.BodyRadius.Returns(0.5f);
        character.CurrentHealth.Returns(dead ? 0u : 10u);
        character.IsDead.Returns(dead);
        _characters[character.Guid] = character;
        return character;
    }

    [Fact]
    public void Return_characters_and_creatures_nearest_first()
    {
        ICreature far = Creature(1, 0f, 2.5f);
        ICharacter near = Character(2, 0f, 1f);
        ICreature middle = Creature(3, 0f, 2f);

        Assert.Equal([near, middle, far], Query.InCircle(Vector3.zero, 3f));
    }

    [Fact]
    public void Break_distance_ties_by_guid()
    {
        ICreature second = Creature(9, 1f, 0f);
        ICreature first = Creature(4, -1f, 0f);

        Assert.Equal([first, second], Query.InCircle(Vector3.zero, 3f));
    }

    [Fact]
    public void Leave_out_dead_units()
    {
        Character(1, 0f, 1f, dead: true);
        Creature(2, 0f, 1f, health: 0);   // a corpse stays in the instance until the corpse remover takes it

        Assert.Empty(Query.InCircle(Vector3.zero, 3f));
    }

    [Fact]
    public void Use_each_units_own_body_radius()
    {
        ICreature big = Creature(1, 4f, 0f, body: 2f);
        Creature(2, 4f, 0.1f, body: 0.5f);

        Assert.Equal([big], Query.InCircle(Vector3.zero, 2.5f));
    }

    [Fact]
    public void Order_a_segment_from_its_start()
    {
        ICreature later = Creature(1, 0f, 8f);
        ICreature sooner = Creature(2, 0f, 3f);

        Assert.Equal([sooner, later], Query.OnSegment(Vector3.zero, new Vector3(0f, 0f, 10f)));
    }

    [Fact]
    public void Order_a_cone_from_its_apex()
    {
        ICreature later = Creature(1, 0f, 2.5f);
        ICreature sooner = Creature(2, 0.3f, 1f);

        Assert.Equal([sooner, later], Query.InCone(Vector3.zero, new Vector3(0f, 0f, 1f), 3f, 90f));
    }
}
