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

    public CharacterCharacterGameState()
    {
        _creatureTrackingSystem = new EntityTrackingSystem(Capacity);
        _creatureTrackingSystem.EntityAdded += OnWorldObjectFound;
        _creatureTrackingSystem.EntityUpdated += OnWorldObjectUpdated;
        _creatureTrackingSystem.EntityRemoved += OnWorldObjectRemoved;

        _characterTrackingSystem = new EntityTrackingSystem(Capacity);
        _characterTrackingSystem.EntityAdded += OnWorldObjectFound;
        _characterTrackingSystem.EntityUpdated += OnWorldObjectUpdated;
        _characterTrackingSystem.EntityRemoved += OnWorldObjectRemoved;

        _worldObjectTrackingSystem = new EntityTrackingSystem(Capacity);
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
        Update(creatures, characters, worldObjects, new Dictionary<ObjectGuid, GameEntityFields>());
    }

    public void Update(
        Dictionary<ObjectGuid, ICreature> creatures,
        Dictionary<ObjectGuid, ICharacter> characters,
        List<IWorldObject> worldObjects,
        IReadOnlyDictionary<ObjectGuid, GameEntityFields> frameDirtyFields)
    {
        NewObjects.Clear();
        UpdatedObjects.Clear();
        RemovedObjects.Clear();

        _creatureTrackingSystem.Update(creatures.Values, frameDirtyFields);
        _characterTrackingSystem.Update(characters.Values, frameDirtyFields);
        _worldObjectTrackingSystem.Update(worldObjects, frameDirtyFields);
    }

    #region Events

    private void OnWorldObjectRemoved(ObjectGuid obj) => RemovedObjects.Add(obj);

    private void OnWorldObjectUpdated(ObjectGuid obj, GameEntityFields fields) => UpdatedObjects.Add((obj, fields));

    private void OnWorldObjectFound(ObjectGuid obj) => NewObjects.Add(obj);

    #endregion
}
