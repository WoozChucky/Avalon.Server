using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.State;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Entities;

public class CharacterCharacterGameStateShould
{
    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    private static Creature MakeRealCreature(uint id, Vector3 position = default, uint health = 100)
    {
        var c = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            Health = health,
            MoveState = MoveState.Idle
        };
        c.Position = position;
        c.CurrentHealth = health;
        c.Velocity = Vector3.zero;
        c.Orientation = Vector3.zero;
        c.ConsumeDirtyFields(); // clear construction dirty so tests start clean
        return c;
    }

    private static CharacterEntity MakeRealCharacter(uint id, Vector3 position = default, uint health = 100)
    {
        var c = new CharacterEntity
        {
            Guid = new ObjectGuid(ObjectType.Character, id),
            Data = new Character { Id = new CharacterId(id), Name = "Tester" + id, Map = 1 },
            MoveState = MoveState.Idle
        };
        c.Position = position;
        c.CurrentHealth = health;
        c.Velocity = Vector3.zero;
        c.MoveState = MoveState.Idle;
        c.ConsumeDirtyFields();
        return c;
    }

    private static Dictionary<ObjectGuid, ICreature> AsCreatureDict(params Creature[] creatures)
        => creatures.ToDictionary(c => c.Guid, c => (ICreature)c);

    private static Dictionary<ObjectGuid, ICharacter> AsCharacterDict(params CharacterEntity[] characters)
        => characters.ToDictionary(c => c.Guid, c => (ICharacter)c);

    private static Dictionary<ObjectGuid, GameEntityFields> EmptyDirty() => new();

    private static readonly ObjectGuid Viewer = new(ObjectType.Character, 593_999u);
    private static readonly InterestRange Range = new(60f, 10f);

    /// <summary>Updates the state for a watcher (<see cref="Viewer" /> unless named) at the origin unless placed.</summary>
    private static void Watch(CharacterCharacterGameState state,
        Dictionary<ObjectGuid, ICreature> creatures,
        Dictionary<ObjectGuid, ICharacter> characters,
        List<IWorldObject> worldObjects,
        IReadOnlyDictionary<ObjectGuid, GameEntityFields> dirty,
        Vector3 at = default,
        ObjectGuid? watcher = null)
        => state.Update(watcher ?? Viewer, at, Range, creatures, characters, worldObjects, dirty);

    private static IWorldObject MakeProjectile(uint id, Vector3 position)
    {
        var projectile = Substitute.For<IWorldObject>();
        projectile.Guid.Returns(new ObjectGuid(ObjectType.SpellProjectile, id));
        projectile.Position.Returns(position);
        return projectile;
    }

    private static Dictionary<ObjectGuid, GameEntityFields> DirtyFrom(params Creature[] creatures)
    {
        var map = new Dictionary<ObjectGuid, GameEntityFields>();
        foreach (var c in creatures)
        {
            var dirty = c.ConsumeDirtyFields();
            if (dirty != GameEntityFields.None)
                map[c.Guid] = dirty;
        }
        return map;
    }

    // ──────────────────────────────────────────────
    // NewObjects — creature tracking
    // ──────────────────────────────────────────────

    [Fact]
    public void NewObjects_ContainsGuid_WhenCreatureSeenFirstTime()
    {
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(1u);

        Watch(state, AsCreatureDict(creature), [], [], EmptyDirty());

        Assert.Contains(creature.Guid, state.NewObjects);
    }

    [Fact]
    public void NewObjects_IsEmpty_WhenSameCreatureSeenTwice()
    {
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(1u);
        Watch(state, AsCreatureDict(creature), [], [], EmptyDirty());

        Watch(state, AsCreatureDict(creature), [], [], EmptyDirty());

        Assert.Empty(state.NewObjects);
    }

    [Fact]
    public void NewObjects_ContainsOnlyFreshCreatures()
    {
        var state = new CharacterCharacterGameState();
        var old = MakeRealCreature(1u);
        Watch(state, AsCreatureDict(old), [], [], EmptyDirty());

        var fresh = MakeRealCreature(2u);
        Watch(state, AsCreatureDict(old, fresh), [], [], EmptyDirty());

        Assert.Single(state.NewObjects);
        Assert.Contains(fresh.Guid, state.NewObjects);
    }

    // ──────────────────────────────────────────────
    // NewObjects — character tracking
    // ──────────────────────────────────────────────

    [Fact]
    public void NewObjects_ContainsCharacterGuid_WhenSeenFirstTime()
    {
        var state = new CharacterCharacterGameState();
        var character = MakeRealCharacter(1u);

        Watch(state, [], AsCharacterDict(character), [], EmptyDirty());

        Assert.Contains(character.Guid, state.NewObjects);
    }

    // ──────────────────────────────────────────────
    // RemovedObjects
    // ──────────────────────────────────────────────

    [Fact]
    public void RemovedObjects_ContainsGuid_WhenCreatureDisappears()
    {
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(1u);
        Watch(state, AsCreatureDict(creature), [], [], EmptyDirty());

        Watch(state, [], [], [], EmptyDirty());

        Assert.Contains(creature.Guid, state.RemovedObjects);
    }

    [Fact]
    public void RemovedObjects_IsEmpty_WhenCreatureStillPresent()
    {
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(1u);
        Watch(state, AsCreatureDict(creature), [], [], EmptyDirty());

        Watch(state, AsCreatureDict(creature), [], [], EmptyDirty());

        Assert.Empty(state.RemovedObjects);
    }

    [Fact]
    public void RemovedObjects_ContainsCharacterGuid_WhenCharacterDisappears()
    {
        var state = new CharacterCharacterGameState();
        var character = MakeRealCharacter(1u);
        Watch(state, [], AsCharacterDict(character), [], EmptyDirty());

        Watch(state, [], [], [], EmptyDirty());

        Assert.Contains(character.Guid, state.RemovedObjects);
    }

    // ──────────────────────────────────────────────
    // UpdatedObjects — dirty map driven
    // ──────────────────────────────────────────────

    [Fact]
    public void UpdatedObjects_ContainsPositionFlag_WhenCreaturePositionChanges()
    {
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(1u, position: Vector3.zero);
        Watch(state, AsCreatureDict(creature), [], [], EmptyDirty());

        creature.Position = new Vector3(10, 0, 10);
        var frameDirty = DirtyFrom(creature);
        Watch(state, AsCreatureDict(creature), [], [], frameDirty);

        var updated = state.UpdatedObjects.FirstOrDefault(o => o.Guid == creature.Guid);
        Assert.NotEqual(default, updated);
        Assert.True((updated.Fields & GameEntityFields.Position) != 0);
    }

    [Fact]
    public void UpdatedObjects_ContainsCurrentHealthFlag_WhenHealthChanges()
    {
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(2u, health: 100);
        Watch(state, AsCreatureDict(creature), [], [], EmptyDirty());

        creature.CurrentHealth = 80u;
        var frameDirty = DirtyFrom(creature);
        Watch(state, AsCreatureDict(creature), [], [], frameDirty);

        var updated = state.UpdatedObjects.FirstOrDefault(o => o.Guid == creature.Guid);
        Assert.True((updated.Fields & GameEntityFields.CurrentHealth) != 0);
    }

    [Fact]
    public void UpdatedObjects_IsEmpty_WhenNothingChanges()
    {
        // Key regression guard: idle entities must produce zero UpdatedObjects entries
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(3u);
        Watch(state, AsCreatureDict(creature), [], [], EmptyDirty());

        // No mutations, empty dirty map
        Watch(state, AsCreatureDict(creature), [], [], EmptyDirty());

        Assert.Empty(state.UpdatedObjects);
    }

    // ──────────────────────────────────────────────
    // State cleared between calls
    // ──────────────────────────────────────────────

    [Fact]
    public void NewObjects_ClearedOnSubsequentUpdate()
    {
        var state = new CharacterCharacterGameState();
        var c1 = MakeRealCreature(1u);
        var c2 = MakeRealCreature(2u);
        Watch(state, AsCreatureDict(c1), [], [], EmptyDirty());
        Watch(state, AsCreatureDict(c1, c2), [], [], EmptyDirty());

        Assert.DoesNotContain(c1.Guid, state.NewObjects);
        Assert.Contains(c2.Guid, state.NewObjects);
    }

    [Fact]
    public void Update_DoesNotThrow_WithAllEmptyInputs()
    {
        var state = new CharacterCharacterGameState();
        var ex = Record.Exception(() => Watch(state, [], [], [], EmptyDirty()));
        Assert.Null(ex);
    }

    [Fact]
    public void Update_DoesNotThrow_WithMultipleCreaturesAndCharacters()
    {
        var state = new CharacterCharacterGameState();
        var creatures = AsCreatureDict(MakeRealCreature(1u), MakeRealCreature(2u));
        var characters = AsCharacterDict(MakeRealCharacter(1u), MakeRealCharacter(2u));

        var ex = Record.Exception(() => Watch(state, creatures, characters, [], EmptyDirty()));
        Assert.Null(ex);
    }

    // ──────────────────────────────────────────────
    // Interest range (#593)
    // ──────────────────────────────────────────────

    [Fact]
    public void Not_track_a_creature_beyond_the_radius()
    {
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(1u, new Vector3(100, 0, 0));

        Watch(state, AsCreatureDict(creature), [], [], EmptyDirty());

        Assert.Empty(state.NewObjects);
    }

    [Fact]
    public void Track_a_creature_once_it_comes_inside_the_radius()
    {
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(1u, new Vector3(100, 0, 0));
        var creatures = AsCreatureDict(creature);
        Watch(state, creatures, [], [], EmptyDirty());

        creature.Position = new Vector3(50, 0, 0);
        Watch(state, creatures, [], [], EmptyDirty());

        Assert.Equal([creature.Guid], state.NewObjects);
    }

    [Fact]
    public void Keep_a_tracked_creature_between_the_radius_and_the_margin()
    {
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(1u, new Vector3(50, 0, 0));
        var creatures = AsCreatureDict(creature);
        Watch(state, creatures, [], [], EmptyDirty());

        creature.Position = new Vector3(65, 0, 0);
        Watch(state, creatures, [], [], EmptyDirty());

        Assert.Empty(state.RemovedObjects);
        Assert.Empty(state.NewObjects);
    }

    [Fact]
    public void Remove_a_tracked_creature_beyond_radius_plus_margin()
    {
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(1u, new Vector3(50, 0, 0));
        var creatures = AsCreatureDict(creature);
        Watch(state, creatures, [], [], EmptyDirty());

        creature.Position = new Vector3(75, 0, 0);
        Watch(state, creatures, [], [], EmptyDirty());

        Assert.Equal([creature.Guid], state.RemovedObjects);
    }

    [Fact]
    public void Re_add_a_creature_that_comes_back()
    {
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(1u, new Vector3(50, 0, 0));
        var creatures = AsCreatureDict(creature);
        Watch(state, creatures, [], [], EmptyDirty());
        creature.Position = new Vector3(75, 0, 0);
        Watch(state, creatures, [], [], EmptyDirty());

        creature.Position = new Vector3(50, 0, 0);
        Watch(state, creatures, [], [], EmptyDirty());

        Assert.Equal([creature.Guid], state.NewObjects);
    }

    [Fact]
    public void Measure_from_the_watchers_own_position()
    {
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(1u, new Vector3(500, 0, 0));

        Watch(state, AsCreatureDict(creature), [], [], EmptyDirty(), at: new Vector3(480, 0, 0));

        Assert.Equal([creature.Guid], state.NewObjects);
    }

    [Fact]
    public void Always_track_the_watchers_own_character()
    {
        var state = new CharacterCharacterGameState();
        var self = MakeRealCharacter(1u, new Vector3(1e6f, 0, 0));

        Watch(state, [], AsCharacterDict(self), [], EmptyDirty(), at: new Vector3(float.NaN, 0, 0), watcher: self.Guid);

        Assert.Equal([self.Guid], state.NewObjects);
    }

    [Fact]
    public void Show_an_unplaced_watcher_only_itself()
    {
        var state = new CharacterCharacterGameState();
        var self = MakeRealCharacter(1u);
        var other = MakeRealCharacter(2u);
        var creature = MakeRealCreature(3u);
        var unplaced = new Vector3(float.NaN, 0, 0);

        var ex = Record.Exception(() => Watch(state, AsCreatureDict(creature), AsCharacterDict(self, other),
            [MakeProjectile(4u, Vector3.zero)], EmptyDirty(), at: unplaced, watcher: self.Guid));

        Assert.Null(ex);
        Assert.Equal([self.Guid], state.NewObjects);
    }

    [Fact]
    public void Filter_world_objects_the_same_way()
    {
        var state = new CharacterCharacterGameState();
        IWorldObject projectile = MakeProjectile(1u, new Vector3(100, 0, 0));
        List<IWorldObject> worldObjects = [projectile];
        Watch(state, [], [], worldObjects, EmptyDirty());
        Assert.Empty(state.NewObjects);

        projectile.Position.Returns(new Vector3(50, 0, 0));
        Watch(state, [], [], worldObjects, EmptyDirty());

        Assert.Equal([projectile.Guid], state.NewObjects);
    }

    [Fact]
    public void Allocate_nothing_in_steady_state()
    {
        var state = new CharacterCharacterGameState();
        var creatures = new Dictionary<ObjectGuid, ICreature>();
        for (uint i = 0; i < 50; i++)
        {
            var creature = MakeRealCreature(i + 1, new Vector3(i % 2 == 0 ? 10f : 200f, 0, 0));
            creatures[creature.Guid] = creature;
        }

        var characters = new Dictionary<ObjectGuid, ICharacter>();
        for (uint i = 0; i < 10; i++)
        {
            var character = MakeRealCharacter(i + 1, new Vector3(i % 2 == 0 ? 10f : 200f, 0, 0));
            characters[character.Guid] = character;
        }

        List<IWorldObject> worldObjects = [];
        var dirty = EmptyDirty();
        Watch(state, creatures, characters, worldObjects, dirty);
        Assert.Equal(30, state.NewObjects.Count); // the 25 creatures and 5 characters in range
        Watch(state, creatures, characters, worldObjects, dirty);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int tick = 0; tick < 100; tick++)
            Watch(state, creatures, characters, worldObjects, dirty);

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Empty(state.NewObjects);
        Assert.Empty(state.RemovedObjects);
    }
}
