using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;

namespace Avalon.World.Entities;

public class EntityTrackingSystem
{
    public event Action<IGameEntity>? EntityAdded;
    public event Action<IGameEntity>? EntityRemoved;
    public event Action<IGameEntity, GameEntityFields>? EntityUpdated;
    
    private readonly IDictionary<ulong, IGameEntity> _entities;
    
    public EntityTrackingSystem(int capacity)
    {
        _entities = new Dictionary<ulong, IGameEntity>(capacity);
    }

    public void Update(IEnumerable<IGameEntity> entities)
    {
        var newEntityIds = new HashSet<ulong>();

        // First pass: Identify new and updated entities
        foreach (var gameEntity in entities)
        {
            newEntityIds.Add(gameEntity.Id);

            if (!_entities.TryGetValue(gameEntity.Id, out var existingEntity))
            {
                switch (gameEntity)
                {
                    case ICharacter character:
                        _entities[gameEntity.Id] = new CharacterEntity
                        {
                            Id = gameEntity.Id,
                            Position = gameEntity.Position,
                            Orientation = gameEntity.Orientation,
                            Velocity = gameEntity.Velocity,
                            MoveState = gameEntity.MoveState
                        };
                        
                        break;
                    case ICreature creature:
                        _entities[gameEntity.Id] = new Creature
                        {
                            Id = gameEntity.Id,
                            Position = gameEntity.Position,
                            Orientation = gameEntity.Orientation,
                            Velocity = gameEntity.Velocity,
                            MoveState = gameEntity.MoveState
                        };
                        break;
                }
                EntityAdded?.Invoke(gameEntity);
            }
            else // if (EntityChanged(existingEntity, gameEntity, out var fields))
            {
                var fields = GetChangedFields(existingEntity, gameEntity);
                switch (gameEntity)
                {
                    case ICharacter character:
                        _entities[gameEntity.Id].Position = gameEntity.Position;
                        _entities[gameEntity.Id].Orientation = gameEntity.Orientation;
                        _entities[gameEntity.Id].Velocity = gameEntity.Velocity;
                        _entities[gameEntity.Id].MoveState = gameEntity.MoveState;
                        _entities[gameEntity.Id].CurrentHealth = gameEntity.CurrentHealth;
                        _entities[gameEntity.Id].CurrentPower = gameEntity.CurrentPower;
                        _entities[gameEntity.Id].Level = gameEntity.Level;
                        break;
                    case ICreature creature:
                        _entities[gameEntity.Id].Position = gameEntity.Position;
                        _entities[gameEntity.Id].Orientation = gameEntity.Orientation;
                        _entities[gameEntity.Id].Velocity = gameEntity.Velocity;
                        _entities[gameEntity.Id].MoveState = gameEntity.MoveState;
                        _entities[gameEntity.Id].CurrentHealth = gameEntity.CurrentHealth;
                        _entities[gameEntity.Id].CurrentPower = gameEntity.CurrentPower;
                        _entities[gameEntity.Id].Level = gameEntity.Level;
                        break;
                }
                
                EntityUpdated?.Invoke(gameEntity, fields);
            }
        }

        // Second pass: Identify removed entities
        var removedEntityIds = new List<ulong>();
        foreach (var existingEntityId in _entities.Keys)
        {
            if (!newEntityIds.Contains(existingEntityId))
            {
                removedEntityIds.Add(existingEntityId);
            }
        }

        foreach (var removedEntityId in removedEntityIds)
        {
            if (_entities.TryGetValue(removedEntityId, out var removedEntity))
            {
                EntityRemoved?.Invoke(removedEntity);
                _entities.Remove(removedEntityId);
            }
        }
    }

    private bool EntityChanged(IGameEntity existingEntity, IGameEntity gameEntity, out GameEntityFields fields)
    {
        fields = GetChangedFields(existingEntity, gameEntity);
        var changed = fields != GameEntityFields.None;
        return changed;
    }
    
    private GameEntityFields GetChangedFields(IGameEntity original, IGameEntity updated)
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
