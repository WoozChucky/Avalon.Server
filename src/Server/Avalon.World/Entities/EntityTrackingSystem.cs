using Avalon.Common;
using Avalon.World.Public;
using Avalon.World.Public.Enums;

namespace Avalon.World.Entities;

public class EntityTrackingSystem
{
    private readonly Func<IWorldObject, IWorldObject> _createObjectHandler;
    private readonly Func<IWorldObject, IWorldObject, IWorldObject> _updateObjectHandler;
    private readonly Func<IWorldObject, IWorldObject, GameEntityFields> _changedFieldsHandler;
    public event Action<ObjectGuid>? EntityAdded;
    public event Action<ObjectGuid>? EntityRemoved;
    public event Action<ObjectGuid, GameEntityFields>? EntityUpdated;

    private readonly IDictionary<ObjectGuid, IWorldObject> _objects;

    public EntityTrackingSystem(int capacity,
        Func<IWorldObject, IWorldObject> createObjectHandler,
        Func<IWorldObject, IWorldObject, IWorldObject> updateObjectHandler,
        Func<IWorldObject, IWorldObject, GameEntityFields> changedFieldsHandler)
    {
        _createObjectHandler = createObjectHandler;
        _updateObjectHandler = updateObjectHandler;
        _changedFieldsHandler = changedFieldsHandler;
        _objects = new Dictionary<ObjectGuid, IWorldObject>(capacity);
    }

    public void Update(IEnumerable<IWorldObject> objects)
    {
        var newObjectIds = new HashSet<ObjectGuid>();

        // First pass: Identify new and updated objects
        foreach (var worldObject in objects)
        {
            newObjectIds.Add(worldObject.Guid);

            if (!_objects.TryGetValue(worldObject.Guid, out var existingObject))
            {
                _objects[worldObject.Guid] = _createObjectHandler(worldObject);
                EntityAdded?.Invoke(worldObject.Guid);
                /*
                switch (worldObject)
                {
                    case ICharacter character:
                        _objects[character.Guid] = new CharacterEntity
                        {
                            Guid = character.Guid,
                            Position = character.Position,
                            Orientation = character.Orientation,
                            Velocity = character.Velocity,
                            MoveState = character.MoveState
                        };
                        
                        break;
                    case ICreature creature:
                        _objects[creature.Guid] = new Creature
                        {
                            Guid = creature.Guid,
                            Position = creature.Position,
                            Orientation = creature.Orientation,
                            Velocity = creature.Velocity,
                            MoveState = creature.MoveState
                        };
                        break;
                }
                */
            }
            else // if (EntityChanged(existingEntity, gameEntity, out var fields))
            {
                var fields = _changedFieldsHandler(existingObject, worldObject);
                _objects[worldObject.Guid] = _updateObjectHandler(existingObject, worldObject);
                EntityUpdated?.Invoke(worldObject.Guid, fields);
                /*
                switch (worldObject)
                {
                    case ICharacter character:
                        _units[character.Guid].Position = character.Position;
                        _units[character.Guid].Orientation = character.Orientation;
                        _units[character.Guid].Velocity = character.Velocity;
                        _units[character.Guid].MoveState = character.MoveState;
                        _units[character.Guid].CurrentHealth = character.CurrentHealth;
                        _units[character.Guid].CurrentPower = character.CurrentPower;
                        _units[character.Guid].Level = character.Level;
                        break;
                    case ICreature creature:
                        _units[worldObject.Guid].Position = worldObject.Position;
                        _units[worldObject.Guid].Orientation = worldObject.Orientation;
                        _units[worldObject.Guid].Velocity = worldObject.Velocity;
                        _units[worldObject.Guid].MoveState = worldObject.MoveState;
                        _units[worldObject.Guid].CurrentHealth = worldObject.CurrentHealth;
                        _units[worldObject.Guid].CurrentPower = worldObject.CurrentPower;
                        _units[worldObject.Guid].Level = worldObject.Level;
                        break;
                }
                */
            }
        }

        // Second pass: Identify removed entities
        var removedObjectIds = new List<ObjectGuid>();
        foreach (var existingObjectIds in _objects.Keys)
        {
            if (!newObjectIds.Contains(existingObjectIds))
            {
                removedObjectIds.Add(existingObjectIds);
            }
        }

        foreach (var removedObjectId in removedObjectIds)
        {
            if (_objects.TryGetValue(removedObjectId, out var removedEntity))
            {
                _objects.Remove(removedObjectId);
                EntityRemoved?.Invoke(removedEntity.Guid);
            }
        }
    }

    private bool EntityChanged(IUnit existingObject, IUnit worldObject, out GameEntityFields fields)
    {
        fields = GetChangedFields(existingObject, worldObject);
        var changed = fields != GameEntityFields.None;
        return changed;
    }

    private GameEntityFields GetChangedFields(IUnit original, IUnit updated)
    {
        GameEntityFields fields = 0;

        if (original.Position != updated.Position)
        {
            fields |= GameEntityFields.Position;
        }

        if (original.CurrentHealth != updated.CurrentHealth)
        {
            fields |= GameEntityFields.CurrentHealth;
        }

        if (original.CurrentPower != updated.CurrentPower)
        {
            fields |= GameEntityFields.CurrentPower;
        }

        if (original.Velocity != updated.Velocity)
        {
            fields |= GameEntityFields.Velocity;
        }

        if (original.Orientation != updated.Orientation)
        {
            fields |= GameEntityFields.Orientation;
        }

        if (original.MoveState != updated.MoveState)
        {
            fields |= GameEntityFields.MoveState;
        }

        return fields;
    }


}
