using Avalon.Common;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Spells;

namespace Avalon.World.Entities;

public class CharacterGameState : IGameState
{
    public ISet<ObjectGuid> NewObjects { get; } = new HashSet<ObjectGuid>(100);
    public ISet<(ObjectGuid Guid, GameEntityFields Fields)> UpdatedObjects { get; } = new HashSet<(ObjectGuid Guid, GameEntityFields Fields)>(100);
    public ISet<ObjectGuid> RemovedObjects { get; } = new HashSet<ObjectGuid>(100);
    
    private readonly EntityTrackingSystem _creatureTrackingSystem;
    private readonly EntityTrackingSystem _characterTrackingSystem;
    
    public CharacterGameState()
    {
        _creatureTrackingSystem = new EntityTrackingSystem(
            100, 
            CreateCreature, 
            UpdateCreature, 
            GetUnitChangedFields
        );
        _creatureTrackingSystem.EntityAdded += OnCreatureFound;
        _creatureTrackingSystem.EntityUpdated += OnCreatureUpdated;
        _creatureTrackingSystem.EntityRemoved += OnCreatureRemoved;
        
        _characterTrackingSystem = new EntityTrackingSystem(
            100,
            CreateCharacter,
            UpdateCharacter,
            GetUnitChangedFields
        );
        _characterTrackingSystem.EntityAdded += OnCharacterFound;
        _characterTrackingSystem.EntityUpdated += OnCharacterUpdated;
        _characterTrackingSystem.EntityRemoved += OnCharacterRemoved;
    }

    #region Events

    private void OnCharacterRemoved(ObjectGuid obj)
    {
        RemovedObjects.Add(obj);
    }

    private void OnCharacterUpdated(ObjectGuid obj, GameEntityFields fields)
    {
        UpdatedObjects.Add((obj, fields));
    }

    private void OnCharacterFound(ObjectGuid obj)
    {
        NewObjects.Add(obj);
    }

    private void OnCreatureRemoved(ObjectGuid obj)
    {
        RemovedObjects.Add(obj);   
    }

    private void OnCreatureUpdated(ObjectGuid obj, GameEntityFields fields)
    {
        UpdatedObjects.Add((obj, fields));
    }

    private void OnCreatureFound(ObjectGuid obj)
    {
        NewObjects.Add(obj);
    }

    #endregion

    public void Update(Dictionary<ObjectGuid, ICreature> creatures, Dictionary<ObjectGuid, ICharacter> characters,
        List<ISpellProjectile> activeSpells)
    {
        NewObjects.Clear();
        UpdatedObjects.Clear();
        RemovedObjects.Clear();
        
        _creatureTrackingSystem.Update(creatures.Values);
        _characterTrackingSystem.Update(characters.Values);
    }
    
    private IWorldObject CreateCreature(IWorldObject obj)
    {
        var creature = (obj as ICreature)!;

        var entity = new Creature
        {
            Guid = creature.Guid,
            Position = creature.Position,
            Orientation = creature.Orientation,
            Velocity = creature.Velocity,
            MoveState = creature.MoveState
        };
        
        return entity;
    }
    
    private IWorldObject UpdateCreature(IWorldObject existingObj, IWorldObject updatedObj)
    {
        var existing = (existingObj as ICreature)!;
        var updated = (updatedObj as ICreature)!;
        
        existing.Position = updated.Position;
        existing.Orientation = updated.Orientation;
        existing.Velocity = updated.Velocity;
        existing.MoveState = updated.MoveState;
        existing.CurrentHealth = updated.CurrentHealth;
        existing.CurrentPower = updated.CurrentPower;
        existing.Level = updated.Level;
        
        return existing;
    }
    
    private IWorldObject CreateCharacter(IWorldObject obj)
    {
        var character = (obj as ICharacter)!;

        var entity = new CharacterEntity
        {
            Guid = character.Guid,
            Position = character.Position,
            Orientation = character.Orientation,
            Velocity = character.Velocity,
            MoveState = character.MoveState
        };
        
        return entity;
    }
    
    private IWorldObject UpdateCharacter(IWorldObject existingObj, IWorldObject updatedObj)
    {
        var existing = (existingObj as ICharacter)!;
        var updated = (updatedObj as ICharacter)!;
        
        existing.Position = updated.Position;
        existing.Orientation = updated.Orientation;
        existing.Velocity = updated.Velocity;
        existing.MoveState = updated.MoveState;
        existing.CurrentHealth = updated.CurrentHealth;
        existing.CurrentPower = updated.CurrentPower;
        existing.Level = updated.Level;
        
        return existing;
    }
    
    private GameEntityFields GetUnitChangedFields(IWorldObject existing, IWorldObject updated)
    {
        var existingUnit = (existing as IUnit)!;
        var updatedUnit = (updated as IUnit)!;
        
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
        
        return fields;
    }
}
