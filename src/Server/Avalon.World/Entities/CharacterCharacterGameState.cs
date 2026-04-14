using Avalon.Common;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;

namespace Avalon.World.Entities;

public class CharacterCharacterGameState : ICharacterGameState
{
    private const int Capacity = 100;

    private readonly EntityTrackingSystem _characterTrackingSystem;
    private readonly EntityTrackingSystem _creatureTrackingSystem;
    private readonly EntityTrackingSystem _worldObjectTrackingSystem;

    private readonly List<ObjectGuid> _newObjects = new(Capacity);
    private readonly List<(ObjectGuid Guid, GameEntityFields Fields)> _updatedObjects = new(Capacity);
    private readonly List<ObjectGuid> _removedObjects = new(Capacity);

    public IReadOnlyList<ObjectGuid> NewObjects => _newObjects;
    public IReadOnlyList<(ObjectGuid Guid, GameEntityFields Fields)> UpdatedObjects => _updatedObjects;
    public IReadOnlyList<ObjectGuid> RemovedObjects => _removedObjects;

    public CharacterCharacterGameState()
    {
        _creatureTrackingSystem = new EntityTrackingSystem(Capacity);
        _creatureTrackingSystem.EntityAdded += OnEntityFound;
        _creatureTrackingSystem.EntityUpdated += OnEntityUpdated;
        _creatureTrackingSystem.EntityRemoved += OnEntityRemoved;

        _characterTrackingSystem = new EntityTrackingSystem(Capacity);
        _characterTrackingSystem.EntityAdded += OnEntityFound;
        _characterTrackingSystem.EntityUpdated += OnEntityUpdated;
        _characterTrackingSystem.EntityRemoved += OnEntityRemoved;

        _worldObjectTrackingSystem = new EntityTrackingSystem(Capacity);
        _worldObjectTrackingSystem.EntityAdded += OnEntityFound;
        _worldObjectTrackingSystem.EntityUpdated += OnEntityUpdated;
        _worldObjectTrackingSystem.EntityRemoved += OnEntityRemoved;
    }

    public void Update(
        Dictionary<ObjectGuid, ICreature> creatures,
        Dictionary<ObjectGuid, ICharacter> characters,
        List<IWorldObject> worldObjects,
        IReadOnlyDictionary<ObjectGuid, GameEntityFields> frameDirtyFields)
    {
        _newObjects.Clear();
        _updatedObjects.Clear();
        _removedObjects.Clear();

        _creatureTrackingSystem.Update(creatures.Values, frameDirtyFields);
        _characterTrackingSystem.Update(characters.Values, frameDirtyFields);
        _worldObjectTrackingSystem.Update(worldObjects, frameDirtyFields);
    }

    private void OnEntityRemoved(ObjectGuid guid) => _removedObjects.Add(guid);
    private void OnEntityUpdated(ObjectGuid guid, GameEntityFields fields) => _updatedObjects.Add((guid, fields));
    private void OnEntityFound(ObjectGuid guid) => _newObjects.Add(guid);
}
