using Avalon.Common;
using Avalon.World.Public;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;

namespace Avalon.World.Entities;

public class EntityTrackingSystem(
    int capacity,
    Func<IWorldObject, IWorldObject> createObjectHandler,
    Func<IWorldObject, IWorldObject, IWorldObject> updateObjectHandler,
    Func<IWorldObject, IWorldObject, GameEntityFields> changedFieldsHandler)
{
    private readonly IDictionary<ObjectGuid, IWorldObject>
        _objects = new Dictionary<ObjectGuid, IWorldObject>(capacity);

    public event Action<ObjectGuid>? EntityAdded;
    public event Action<ObjectGuid>? EntityRemoved;
    public event Action<ObjectGuid, GameEntityFields>? EntityUpdated;

    public void Update(IEnumerable<IWorldObject> objects)
    {
        HashSet<ObjectGuid> newObjectIds = [];

        // First pass: Identify new and updated objects
        foreach (IWorldObject worldObject in objects)
        {
            newObjectIds.Add(worldObject.Guid);

            if (!_objects.TryGetValue(worldObject.Guid, out IWorldObject? existingObject))
            {
                _objects[worldObject.Guid] = createObjectHandler(worldObject);
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
                GameEntityFields fields = changedFieldsHandler(existingObject, worldObject);
                _objects[worldObject.Guid] = updateObjectHandler(existingObject, worldObject);
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
        List<ObjectGuid> removedObjectIds = new();
        foreach (ObjectGuid existingObjectIds in _objects.Keys)
        {
            if (!newObjectIds.Contains(existingObjectIds))
            {
                removedObjectIds.Add(existingObjectIds);
            }
        }

        foreach (ObjectGuid removedObjectId in removedObjectIds)
        {
            if (_objects.TryGetValue(removedObjectId, out IWorldObject? removedEntity))
            {
                _objects.Remove(removedObjectId);
                EntityRemoved?.Invoke(removedEntity.Guid);
            }
        }
    }

    private bool EntityChanged(IUnit existingObject, IUnit worldObject, out GameEntityFields fields)
    {
        fields = GetChangedFields(existingObject, worldObject);
        bool changed = fields != GameEntityFields.None;
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
