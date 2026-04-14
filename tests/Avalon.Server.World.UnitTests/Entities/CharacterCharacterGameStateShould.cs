using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
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
            MoveState = MoveState.Idle
        };
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

        state.Update(AsCreatureDict(creature), [], [], EmptyDirty());

        Assert.Contains(creature.Guid, state.NewObjects);
    }

    [Fact]
    public void NewObjects_IsEmpty_WhenSameCreatureSeenTwice()
    {
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(1u);
        state.Update(AsCreatureDict(creature), [], [], EmptyDirty());

        state.Update(AsCreatureDict(creature), [], [], EmptyDirty());

        Assert.Empty(state.NewObjects);
    }

    [Fact]
    public void NewObjects_ContainsOnlyFreshCreatures()
    {
        var state = new CharacterCharacterGameState();
        var old = MakeRealCreature(1u);
        state.Update(AsCreatureDict(old), [], [], EmptyDirty());

        var fresh = MakeRealCreature(2u);
        state.Update(AsCreatureDict(old, fresh), [], [], EmptyDirty());

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

        state.Update([], AsCharacterDict(character), [], EmptyDirty());

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
        state.Update(AsCreatureDict(creature), [], [], EmptyDirty());

        state.Update([], [], [], EmptyDirty());

        Assert.Contains(creature.Guid, state.RemovedObjects);
    }

    [Fact]
    public void RemovedObjects_IsEmpty_WhenCreatureStillPresent()
    {
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(1u);
        state.Update(AsCreatureDict(creature), [], [], EmptyDirty());

        state.Update(AsCreatureDict(creature), [], [], EmptyDirty());

        Assert.Empty(state.RemovedObjects);
    }

    [Fact]
    public void RemovedObjects_ContainsCharacterGuid_WhenCharacterDisappears()
    {
        var state = new CharacterCharacterGameState();
        var character = MakeRealCharacter(1u);
        state.Update([], AsCharacterDict(character), [], EmptyDirty());

        state.Update([], [], [], EmptyDirty());

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
        state.Update(AsCreatureDict(creature), [], [], EmptyDirty());

        creature.Position = new Vector3(10, 0, 10);
        var frameDirty = DirtyFrom(creature);
        state.Update(AsCreatureDict(creature), [], [], frameDirty);

        var updated = state.UpdatedObjects.FirstOrDefault(o => o.Guid == creature.Guid);
        Assert.NotEqual(default, updated);
        Assert.True((updated.Fields & GameEntityFields.Position) != 0);
    }

    [Fact]
    public void UpdatedObjects_ContainsCurrentHealthFlag_WhenHealthChanges()
    {
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(2u, health: 100);
        state.Update(AsCreatureDict(creature), [], [], EmptyDirty());

        creature.CurrentHealth = 80u;
        var frameDirty = DirtyFrom(creature);
        state.Update(AsCreatureDict(creature), [], [], frameDirty);

        var updated = state.UpdatedObjects.FirstOrDefault(o => o.Guid == creature.Guid);
        Assert.True((updated.Fields & GameEntityFields.CurrentHealth) != 0);
    }

    [Fact]
    public void UpdatedObjects_IsEmpty_WhenNothingChanges()
    {
        // Key regression guard: idle entities must produce zero UpdatedObjects entries
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(3u);
        state.Update(AsCreatureDict(creature), [], [], EmptyDirty());

        // No mutations, empty dirty map
        state.Update(AsCreatureDict(creature), [], [], EmptyDirty());

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
        state.Update(AsCreatureDict(c1), [], [], EmptyDirty());
        state.Update(AsCreatureDict(c1, c2), [], [], EmptyDirty());

        Assert.DoesNotContain(c1.Guid, state.NewObjects);
        Assert.Contains(c2.Guid, state.NewObjects);
    }

    [Fact]
    public void Update_DoesNotThrow_WithAllEmptyInputs()
    {
        var state = new CharacterCharacterGameState();
        var ex = Record.Exception(() => state.Update([], [], [], EmptyDirty()));
        Assert.Null(ex);
    }

    [Fact]
    public void Update_DoesNotThrow_WithMultipleCreaturesAndCharacters()
    {
        var state = new CharacterCharacterGameState();
        var creatures = AsCreatureDict(MakeRealCreature(1u), MakeRealCreature(2u));
        var characters = AsCharacterDict(MakeRealCharacter(1u), MakeRealCharacter(2u));

        var ex = Record.Exception(() => state.Update(creatures, characters, [], EmptyDirty()));
        Assert.Null(ex);
    }
}
