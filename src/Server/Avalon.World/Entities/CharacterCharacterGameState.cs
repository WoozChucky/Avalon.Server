using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Telemetry;

namespace Avalon.World.Entities;

/// <summary>
/// What one character's client has been told exists: its replication state. Its instance decides the view again once
/// per broadcast period, on the instance's broadcast tick (<see cref="Update" />), and holds it on the ticks between
/// (<see cref="Hold" />, #981); this decides who is in view, the instance decides when. World-side only (#612):
/// World.Public is the modding API, and a mod able to drive this could change what a player's client is told it can
/// see.
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

    // What the last Reset forgot (#611), kept apart from _removedObjects so a reset reports nothing gone.
    private readonly List<ObjectGuid> _forgotten = new(Capacity);

    // What this tick's view holds (#593), reused every tick so a steady tick allocates nothing.
    private readonly List<IWorldObject> _visibleCreatures = new(Capacity);
    private readonly List<IWorldObject> _visibleCharacters = new(Capacity);
    private readonly List<IWorldObject> _visibleWorldObjects = new(Capacity);

    public IReadOnlyList<ObjectGuid> NewObjects => _newObjects;
    public IReadOnlyList<(ObjectGuid Guid, GameEntityFields Fields)> UpdatedObjects => _updatedObjects;
    public IReadOnlyList<ObjectGuid> RemovedObjects => _removedObjects;

    /// <summary>
    /// Whether the view must be decided on the next tick, whatever the instance's cadence (#981): true for a new state
    /// and after <see cref="Reset" />, until an <see cref="Update" /> completes. A client entering the world or an
    /// instance has been told of nothing there, so its first tick lists it itself and everything in view, as every tick
    /// did before #981, rather than leaving it in an empty world until the instance's next broadcast.
    /// </summary>
    internal bool ScanOwed { get; private set; } = true;

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
        InstanceTickProfile profile = default;
        UpdateProfiled(watcher, watcherPosition, range, creatures, characters, worldObjects, frameDirtyFields, ref profile);
    }

    internal void UpdateProfiled(
        ObjectGuid watcher,
        Vector3 watcherPosition,
        InterestRange range,
        Dictionary<ObjectGuid, ICreature> creatures,
        Dictionary<ObjectGuid, ICharacter> characters,
        List<IWorldObject> worldObjects,
        IReadOnlyDictionary<ObjectGuid, GameEntityFields> frameDirtyFields, ref InstanceTickProfile profile)
    {
        long profileStart = profile.Timestamp();
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

        profile.VisibilityTicks += profile.Elapsed(profileStart);
        if (profile.Enabled)
        {
            profile.CandidateChecks += (long)creatures.Count + characters.Count + worldObjects.Count;
            profile.VisiblePairs += (long)_visibleCreatures.Count + _visibleCharacters.Count + _visibleWorldObjects.Count;
        }
        profileStart = profile.Timestamp();
        _creatureTrackingSystem.Update(_visibleCreatures, frameDirtyFields);
        _characterTrackingSystem.Update(_visibleCharacters, frameDirtyFields);
        _worldObjectTrackingSystem.Update(_visibleWorldObjects, frameDirtyFields);
        profile.TrackingTicks += profile.Elapsed(profileStart);
        if (profile.Enabled)
        {
            profile.Added += _newObjects.Count;
            profile.Updated += _updatedObjects.Count;
            profile.Removed += _removedObjects.Count;
        }

        ScanOwed = false;
    }

    /// <summary>
    /// A tick on which the view is not decided again (#981): what the client has been told stays as it is, and nothing
    /// the last <see cref="Update" /> reported is reported a second time. Updates need no decision of their own: an
    /// entity's changes are sent only on its instance's broadcast tick, which decides the view first.
    /// </summary>
    internal void Hold()
    {
        _newObjects.Clear();
        _updatedObjects.Clear();
        _removedObjects.Clear();
    }

    /// <summary>
    /// A character that has left the instance (#981): if the client was told of it, the removal is reported now, among
    /// this tick's removes, rather than at the view's next decision. A character's guid comes back with it, so a client
    /// still holding it when it came back would never be told of what changed while it was elsewhere. Call after this
    /// tick's <see cref="Update" /> or <see cref="Hold" />, which empty the removes. True when it was reported.
    /// </summary>
    internal bool CharacterLeft(ObjectGuid guid)
    {
        if (!_characterTrackingSystem.Untrack(guid))
            return false;

        _removedObjects.Add(guid);
        return true;
    }

    /// <summary>
    /// Starts over (#611), for a character entering an instance: everything the client was told about is
    /// forgotten, with no removal raised, so the next <see cref="Update" /> adds, in full, whatever is in
    /// view there, the character itself included. Returns what was forgotten, which the client still
    /// holds until it is told to drop it. <see cref="RemovedObjects" /> is left empty, so nothing is
    /// reported gone twice. The list is reused: it is valid until the next call to this.
    /// </summary>
    public IReadOnlyList<ObjectGuid> Reset()
    {
        _newObjects.Clear();
        _updatedObjects.Clear();
        _removedObjects.Clear();

        _forgotten.Clear();

        _creatureTrackingSystem.Forget(_forgotten);
        _characterTrackingSystem.Forget(_forgotten);
        _worldObjectTrackingSystem.Forget(_forgotten);
        ScanOwed = true;
        return _forgotten;
    }

    /// <summary>
    /// Whether the client has been told of this character or creature and not told it is gone, as of the last decision
    /// of its view (auras): what it holds, which since #981 can be up to a broadcast period behind what is in range.
    /// </summary>
    public bool Knows(ObjectGuid guid) => _characterTrackingSystem.IsTracked(guid) || _creatureTrackingSystem.IsTracked(guid);

    // "Already tracked" is the object's own tracking system's set, so a tracked object keeps its margin. The set is
    // asked only for an object in the margin (#982): within the radius or beyond the margin it cannot change the answer.
    private static bool InView(IWorldObject obj, EntityTrackingSystem tracking, ObjectGuid watcher,
        Vector3 watcherPosition, InterestRange range) =>
        Interest.IsVisible(watcher, watcherPosition, obj.Guid, obj.Position, range, new TrackedBy(tracking));

    private readonly struct TrackedBy(EntityTrackingSystem tracking) : Interest.ITrackedSet
    {
        public bool Contains(ObjectGuid obj) => tracking.IsTracked(obj);
    }

    private void OnEntityRemoved(ObjectGuid guid) => _removedObjects.Add(guid);
    private void OnEntityUpdated(ObjectGuid guid, GameEntityFields fields) => _updatedObjects.Add((guid, fields));
    private void OnEntityFound(ObjectGuid guid) => _newObjects.Add(guid);
}
