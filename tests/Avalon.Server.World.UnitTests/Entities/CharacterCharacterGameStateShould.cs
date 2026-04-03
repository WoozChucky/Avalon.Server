using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
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
        return new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            Position = position,
            CurrentHealth = health,
            Health = health,
            Velocity = Vector3.zero,
            Orientation = Vector3.zero,
            MoveState = MoveState.Idle
        };
    }

    private static CharacterEntity MakeRealCharacter(uint id, Vector3 position = default, uint health = 100)
    {
        // Use the parameterless constructor (sets Connection/fields to null, which is fine for tracking tests)
        return new CharacterEntity
        {
            Guid = new ObjectGuid(ObjectType.Character, id),
            Position = position,
            CurrentHealth = health,
            Health = health,
            Velocity = Vector3.zero,
            Orientation = Vector3.zero,
            MoveState = MoveState.Idle
        };
    }

    private static Dictionary<ObjectGuid, ICreature> AsCreatureDict(params Creature[] creatures)
        => creatures.ToDictionary(c => c.Guid, c => (ICreature)c);

    private static Dictionary<ObjectGuid, ICharacter> AsCharacterDict(params CharacterEntity[] characters)
        => characters.ToDictionary(c => c.Guid, c => (ICharacter)c);

    // ──────────────────────────────────────────────
    // NewObjects — creature tracking
    // ──────────────────────────────────────────────

    [Fact]
    public void NewObjects_ContainsGuid_WhenCreatureSeenFirstTime()
    {
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(1u);

        state.Update(AsCreatureDict(creature), [], []);

        Assert.Contains(creature.Guid, state.NewObjects);
    }

    [Fact]
    public void NewObjects_IsEmpty_WhenSameCreatureSeenTwice()
    {
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(1u);
        state.Update(AsCreatureDict(creature), [], []);

        state.Update(AsCreatureDict(creature), [], []);

        Assert.Empty(state.NewObjects);
    }

    [Fact]
    public void NewObjects_ContainsOnlyFreshCreatures()
    {
        var state = new CharacterCharacterGameState();
        var old = MakeRealCreature(1u);
        state.Update(AsCreatureDict(old), [], []);

        var fresh = MakeRealCreature(2u);
        state.Update(AsCreatureDict(old, fresh), [], []);

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

        state.Update([], AsCharacterDict(character), []);

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
        state.Update(AsCreatureDict(creature), [], []);

        state.Update([], [], []); // creature gone

        Assert.Contains(creature.Guid, state.RemovedObjects);
    }

    [Fact]
    public void RemovedObjects_IsEmpty_WhenCreatureStillPresent()
    {
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(1u);
        state.Update(AsCreatureDict(creature), [], []);

        state.Update(AsCreatureDict(creature), [], []);

        Assert.Empty(state.RemovedObjects);
    }

    [Fact]
    public void RemovedObjects_ContainsCharacterGuid_WhenCharacterDisappears()
    {
        var state = new CharacterCharacterGameState();
        var character = MakeRealCharacter(1u);
        state.Update([], AsCharacterDict(character), []);

        state.Update([], [], []);

        Assert.Contains(character.Guid, state.RemovedObjects);
    }

    // ──────────────────────────────────────────────
    // UpdatedObjects — changed fields
    // ──────────────────────────────────────────────

    [Fact]
    public void UpdatedObjects_ContainsGuidWithPositionFlag_WhenCreaturePositionChanges()
    {
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(1u, position: Vector3.zero);
        state.Update(AsCreatureDict(creature), [], []);

        // Now move the creature
        creature.Position = new Vector3(10, 0, 10);
        state.Update(AsCreatureDict(creature), [], []);

        var updated = state.UpdatedObjects.FirstOrDefault(o => o.Guid == creature.Guid);
        Assert.NotEqual(default, updated);
        Assert.True((updated.Fields & GameEntityFields.Position) != 0);
    }

    [Fact]
    public void UpdatedObjects_ContainsCurrentHealthFlag_WhenHealthChanges()
    {
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(2u, health: 100);
        state.Update(AsCreatureDict(creature), [], []);

        creature.CurrentHealth = 80;
        state.Update(AsCreatureDict(creature), [], []);

        var updated = state.UpdatedObjects.FirstOrDefault(o => o.Guid == creature.Guid);
        Assert.True((updated.Fields & GameEntityFields.CurrentHealth) != 0);
    }

    [Fact]
    public void UpdatedObjects_IsEmpty_WhenNothingChanges()
    {
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(3u);
        state.Update(AsCreatureDict(creature), [], []);

        state.Update(AsCreatureDict(creature), [], []);

        // No changes → no UpdatedObjects entries for this creature (zero fields)
        bool hasNonZeroEntry = state.UpdatedObjects
            .Where(o => o.Guid == creature.Guid)
            .Any(o => o.Fields != (GameEntityFields)0);
        Assert.False(hasNonZeroEntry);
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
        state.Update(AsCreatureDict(c1), [], []);          // c1 is new
        state.Update(AsCreatureDict(c1, c2), [], []);      // c2 is new, c1 no longer new

        Assert.DoesNotContain(c1.Guid, state.NewObjects);
        Assert.Contains(c2.Guid, state.NewObjects);
    }

    [Fact]
    public void Update_DoesNotThrow_WithAllEmptyInputs()
    {
        var state = new CharacterCharacterGameState();
        var ex = Record.Exception(() => state.Update([], [], []));
        Assert.Null(ex);
    }

    [Fact]
    public void Update_DoesNotThrow_WithMultipleCreaturesAndCharacters()
    {
        var state = new CharacterCharacterGameState();
        var creatures = AsCreatureDict(MakeRealCreature(1u), MakeRealCreature(2u));
        var characters = AsCharacterDict(MakeRealCharacter(1u), MakeRealCharacter(2u));

        var ex = Record.Exception(() => state.Update(creatures, characters, []));
        Assert.Null(ex);
    }
}
