using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Abilities;
using Avalon.World.Abilities.Targeting;
using Avalon.World.Entities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Maps;
using Avalon.World.Public.Units;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Abilities;

/// <summary>
/// A real hit query over units the test places, a substitute combat service to record who was hit
/// and in what order, and a navigator that lets every ray through unless a test says otherwise.
/// </summary>
internal sealed class TestArena : IAbilityArena
{
    private readonly Dictionary<ObjectGuid, ICharacter> _characters = new();
    private readonly Dictionary<ObjectGuid, ICreature> _creatures = new();
    private uint _nextCreature = 1000;

    public TestArena(MapType mapType = MapType.Normal)
    {
        MapType = mapType;
        Hits = new UnitHitQuery(_characters, _creatures);
        Navigator.RaycastWalkable(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns(ci => ci.ArgAt<Vector3>(1));
    }

    public MapType MapType { get; }
    public IHitQuery Hits { get; }
    public ICombatService CombatService { get; } = Substitute.For<ICombatService>();
    public IMapNavigator Navigator { get; } = Substitute.For<IMapNavigator>();
    public List<(Vector3 Origin, Vector3? Direction, Vector3? Centre)> Fired { get; } = [];

    public IMapNavigator GetNavigatorForPosition(Vector3 position) => Navigator;

    public void BroadcastAbilityFired(IUnit caster, IAbility ability, Vector3 origin, Vector3? direction, Vector3? centre) =>
        Fired.Add((origin, direction, centre));

    public CharacterEntity Player(uint id, float x, float z, bool pvp = false)
    {
        CharacterEntity character = TestCharacters.New(id);
        character.Data!.PvpEnabled = pvp;
        character.Health = 100;
        character.CurrentHealth = 50;
        character.Position = new Vector3(x, 0f, z);
        _characters[character.Guid] = character;
        return character;
    }

    public ICreature Creature(float x, float z, float body = 0.5f, bool invulnerable = false, uint health = 10)
    {
        var creature = Substitute.For<ICreature>();
        creature.Guid.Returns(new ObjectGuid(ObjectType.Creature, _nextCreature++));
        creature.Position.Returns(new Vector3(x, 0f, z));
        creature.BodyRadius.Returns(body);
        creature.CurrentHealth.Returns(health);
        creature.Invulnerable.Returns(invulnerable);
        _creatures[creature.Guid] = creature;
        return creature;
    }

    /// <summary>Every unit damaged, in order.</summary>
    public List<IUnit> Damaged() => CombatService.ReceivedCalls()
        .Where(c => c.GetMethodInfo().Name == nameof(ICombatService.ApplyDamage))
        .Select(c => (IUnit)c.GetArguments()[1]!)
        .ToList();

    /// <summary>Every unit healed, in order.</summary>
    public List<IUnit> Healed() => CombatService.ReceivedCalls()
        .Where(c => c.GetMethodInfo().Name == nameof(ICombatService.ApplyHeal))
        .Select(c => (IUnit)c.GetArguments()[1]!)
        .ToList();
}
