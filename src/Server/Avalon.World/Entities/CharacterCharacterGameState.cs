using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;

namespace Avalon.World.Entities;

/// <summary>
/// What one character's client has been told exists: its replication state, which its instance diffs
/// every tick. World-side only (#612): World.Public is the modding API, and a mod able to drive this
/// could change what a player's client is told it can see.
/// </summary>
public class CharacterCharacterGameState
{
    private const int Capacity = 100;

    private readonly EntityTrackingSystem _characterTrackingSystem;
    private readonly EntityTrackingSystem _creatureTrackingSystem;
    private readonly EntityTrackingSystem _worldObjectTrackingSystem;

    private readonly List<ObjectGuid> _newObjects = new(Capacity);
    private readonly List<(ObjectGuid Guid, GameEntityFields Fields)> _updatedObjects = new(Capacity);
    private readonly List<ObjectGuid> _removedObjects = new(Capacity);

    // What this tick's view holds (#593), reused every tick so a steady tick allocates nothing.
    private readonly List<IWorldObject> _visibleCreatures = new(Capacity);
    private readonly List<IWorldObject> _visibleCharacters = new(Capacity);
    private readonly List<IWorldObject> _visibleWorldObjects = new(Capacity);

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

    /// <summary>
    /// Diffs this tick's objects against what the client already sees. Only the objects in its view
    /// count (#593): the watcher's own character always, every other one by <paramref name="range" />
    /// from <paramref name="watcherPosition" />, on X/Z.
    /// </summary>
    public void Update(
        ObjectGuid watcher,
        Vector3 watcherPosition,
        InterestRange range,
        Dictionary<ObjectGuid, ICreature> creatures,
        Dictionary<ObjectGuid, ICharacter> characters,
        List<IWorldObject> worldObjects,
        IReadOnlyDictionary<ObjectGuid, GameEntityFields> frameDirtyFields)
    {
        _newObjects.Clear();
        _updatedObjects.Clear();
        _removedObjects.Clear();

        _visibleCreatures.Clear();
        foreach (ICreature creature in creatures.Values)
        {
            if (InView(creature, _creatureTrackingSystem, watcher, watcherPosition, range))
                _visibleCreatures.Add(creature);
        }

        _visibleCharacters.Clear();
        foreach (ICharacter character in characters.Values)
        {
            if (InView(character, _characterTrackingSystem, watcher, watcherPosition, range))
                _visibleCharacters.Add(character);
        }

        _visibleWorldObjects.Clear();
        foreach (IWorldObject worldObject in worldObjects)
        {
            if (InView(worldObject, _worldObjectTrackingSystem, watcher, watcherPosition, range))
                _visibleWorldObjects.Add(worldObject);
        }

        _creatureTrackingSystem.Update(_visibleCreatures, frameDirtyFields);
        _characterTrackingSystem.Update(_visibleCharacters, frameDirtyFields);
        _worldObjectTrackingSystem.Update(_visibleWorldObjects, frameDirtyFields);
    }

    // "Already tracked" is the object's own tracking system's set, so a tracked object keeps its margin.
    private static bool InView(IWorldObject obj, EntityTrackingSystem tracking, ObjectGuid watcher,
        Vector3 watcherPosition, InterestRange range) =>
        Interest.IsVisible(watcher, watcherPosition, obj.Guid, obj.Position, tracking.IsTracked(obj.Guid), range);

    private void OnEntityRemoved(ObjectGuid guid) => _removedObjects.Add(guid);
    private void OnEntityUpdated(ObjectGuid guid, GameEntityFields fields) => _updatedObjects.Add((guid, fields));
    private void OnEntityFound(ObjectGuid guid) => _newObjects.Add(guid);
}
