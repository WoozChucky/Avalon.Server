using Avalon.Common;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;

namespace Avalon.World.Entities;

public class CharacterCharacterGameState : ICharacterGameState
{
    private const int Capacity = 100;
    private readonly EntityTrackingSystem _characterTrackingSystem;
    private readonly EntityTrackingSystem _creatureTrackingSystem;
    private readonly EntityTrackingSystem _worldObjectTrackingSystem;

    public CharacterCharacterGameState()
    {
        _creatureTrackingSystem = new EntityTrackingSystem(
            Capacity,
            CreateCreature,
            UpdateCreature,
            GetUnitChangedFields
        );
        _creatureTrackingSystem.EntityAdded += OnWorldObjectFound;
        _creatureTrackingSystem.EntityUpdated += OnWorldObjectUpdated;
        _creatureTrackingSystem.EntityRemoved += OnWorldObjectRemoved;

        _characterTrackingSystem = new EntityTrackingSystem(
            Capacity,
            CreateCharacter,
            UpdateCharacter,
            GetUnitChangedFields
        );
        _characterTrackingSystem.EntityAdded += OnWorldObjectFound;
        _characterTrackingSystem.EntityUpdated += OnWorldObjectUpdated;
        _characterTrackingSystem.EntityRemoved += OnWorldObjectRemoved;

        _worldObjectTrackingSystem = new EntityTrackingSystem(
            Capacity,
            CreateWorldObject,
            UpdateWorldObject,
            GetWorldObjectChangedFields
        );
        _worldObjectTrackingSystem.EntityAdded += OnWorldObjectFound;
        _worldObjectTrackingSystem.EntityUpdated += OnWorldObjectUpdated;
        _worldObjectTrackingSystem.EntityRemoved += OnWorldObjectRemoved;
    }

    public ISet<ObjectGuid> NewObjects { get; } = new HashSet<ObjectGuid>(Capacity);

    public ISet<(ObjectGuid Guid, GameEntityFields Fields)> UpdatedObjects { get; } =
        new HashSet<(ObjectGuid Guid, GameEntityFields Fields)>(Capacity);

    public ISet<ObjectGuid> RemovedObjects { get; } = new HashSet<ObjectGuid>(Capacity);

    public void Update(
        Dictionary<ObjectGuid, ICreature> creatures,
        Dictionary<ObjectGuid, ICharacter> characters,
        List<IWorldObject> worldObjects)
    {
        NewObjects.Clear();
        UpdatedObjects.Clear();
        RemovedObjects.Clear();

        _creatureTrackingSystem.Update(creatures.Values);
        _characterTrackingSystem.Update(characters.Values);
        _worldObjectTrackingSystem.Update(worldObjects);
    }

    private IWorldObject CreateCreature(IWorldObject obj)
    {
        ICreature creature = (obj as ICreature)!;

        Creature entity = new()
        {
            Guid = creature.Guid,
            Position = creature.Position,
            Orientation = creature.Orientation,
            Velocity = creature.Velocity,
            MoveState = creature.MoveState,
            Health = creature.Health,
            CurrentHealth = creature.CurrentHealth,
            PowerType = creature.PowerType,
            Power = creature.Power,
            CurrentPower = creature.CurrentPower
        };

        return entity;
    }

    private IWorldObject UpdateCreature(IWorldObject existingObj, IWorldObject updatedObj)
    {
        ICreature existing = (existingObj as ICreature)!;
        ICreature updated = (updatedObj as ICreature)!;

        existing.Position = updated.Position;
        existing.Orientation = updated.Orientation;
        existing.Velocity = updated.Velocity;
        existing.MoveState = updated.MoveState;
        existing.Health = updated.Health;
        existing.CurrentHealth = updated.CurrentHealth;
        existing.PowerType = updated.PowerType;
        existing.Power = updated.Power;
        existing.CurrentPower = updated.CurrentPower;
        existing.Level = updated.Level;


        return existing;
    }

    private IWorldObject CreateCharacter(IWorldObject obj)
    {
        ICharacter character = (obj as ICharacter)!;

        CharacterEntity entity = new()
        {
            Guid = character.Guid,
            Position = character.Position,
            Orientation = character.Orientation,
            Velocity = character.Velocity,
            MoveState = character.MoveState,
            Health = character.Health,
            CurrentHealth = character.CurrentHealth,
            PowerType = character.PowerType,
            Power = character.Power,
            CurrentPower = character.CurrentPower,
            Level = character.Level,
            Experience = character.Experience,
            RequiredExperience = character.RequiredExperience
        };

        return entity;
    }

    private IWorldObject UpdateCharacter(IWorldObject existingObj, IWorldObject updatedObj)
    {
        ICharacter existing = (existingObj as ICharacter)!;
        ICharacter updated = (updatedObj as ICharacter)!;

        existing.Position = updated.Position;
        existing.Orientation = updated.Orientation;
        existing.Velocity = updated.Velocity;
        existing.MoveState = updated.MoveState;
        existing.Health = updated.Health;
        existing.CurrentHealth = updated.CurrentHealth;
        existing.PowerType = updated.PowerType;
        existing.Power = updated.Power;
        existing.CurrentPower = updated.CurrentPower;
        existing.Level = updated.Level;
        existing.Experience = updated.Experience;
        existing.RequiredExperience = updated.RequiredExperience;

        return existing;
    }

    private GameEntityFields GetUnitChangedFields(IWorldObject existing, IWorldObject updated)
    {
        IUnit existingUnit = (existing as IUnit)!;
        IUnit updatedUnit = (updated as IUnit)!;

        GameEntityFields fields = 0;

        if (existingUnit.Position != updatedUnit.Position)
        {
            fields |= GameEntityFields.Position;
        }

        if (existingUnit.CurrentHealth != updatedUnit.CurrentHealth)
        {
            fields |= GameEntityFields.CurrentHealth;
        }

        if (existingUnit.CurrentPower != updatedUnit.CurrentPower)
        {
            fields |= GameEntityFields.CurrentPower;
        }

        if (existingUnit.Velocity != updatedUnit.Velocity)
        {
            fields |= GameEntityFields.Velocity;
        }

        if (existingUnit.Orientation != updatedUnit.Orientation)
        {
            fields |= GameEntityFields.Orientation;
        }

        if (existingUnit.MoveState != updatedUnit.MoveState)
        {
            fields |= GameEntityFields.MoveState;
        }

        if (existingUnit.Level != updatedUnit.Level)
        {
            fields |= GameEntityFields.Level;
        }

        if (existingUnit.Power != updatedUnit.Power)
        {
            fields |= GameEntityFields.Power;
        }

        if (existingUnit.Health != updatedUnit.Health)
        {
            fields |= GameEntityFields.Health;
        }

        // Character specific fields
        if (existingUnit is ICharacter existingCharacter && updatedUnit is ICharacter updatedCharacter)
        {
            if (existingCharacter.Experience != updatedCharacter.Experience)
            {
                fields |= GameEntityFields.Experience;
            }

            if (existingCharacter.RequiredExperience != updatedCharacter.RequiredExperience)
            {
                fields |= GameEntityFields.RequiredExperience;
            }
        }

        return fields;
    }

    private IWorldObject CreateWorldObject(IWorldObject obj)
    {
        if (obj is not SpellScript spell)
        {
            throw new InvalidCastException("Object is not a spell");
        }

        return spell.Clone();
    }

    private IWorldObject UpdateWorldObject(IWorldObject existingObj, IWorldObject updatedObj)
    {
        if (existingObj is not SpellScript existingSpell)
        {
            throw new InvalidCastException("Object is not a spell");
        }

        if (updatedObj is not SpellScript updatedSpell)
        {
            throw new InvalidCastException("Object is not a spell");
        }

        existingSpell.Position = updatedSpell.Position;
        existingSpell.Velocity = updatedSpell.Velocity;
        existingSpell.Orientation = updatedSpell.Orientation;

        return existingSpell;
    }

    private GameEntityFields GetWorldObjectChangedFields(IWorldObject existing, IWorldObject updated)
    {
        SpellScript existingUnit = existing as SpellScript ?? throw new InvalidCastException("Object is not a spell");
        SpellScript updatedUnit = updated as SpellScript ?? throw new InvalidCastException("Object is not a spell");

        GameEntityFields fields = 0;

        if (existingUnit.Position != updatedUnit.Position)
        {
            fields |= GameEntityFields.Position;
        }

        if (existingUnit.Velocity != updatedUnit.Velocity)
        {
            fields |= GameEntityFields.Velocity;
        }

        if (existingUnit.Orientation != updatedUnit.Orientation)
        {
            fields |= GameEntityFields.Orientation;
        }

        return fields;
    }

    #region Events

    private void OnWorldObjectRemoved(ObjectGuid obj) => RemovedObjects.Add(obj);

    private void OnWorldObjectUpdated(ObjectGuid obj, GameEntityFields fields) => UpdatedObjects.Add((obj, fields));

    private void OnWorldObjectFound(ObjectGuid obj) => NewObjects.Add(obj);

    #endregion
}
