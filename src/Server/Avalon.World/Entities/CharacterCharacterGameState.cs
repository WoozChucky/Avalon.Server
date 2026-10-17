using System.Runtime.InteropServices;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Doodads;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Quests;
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
    private readonly EntityTrackingSystem _doodadTrackingSystem;

    private readonly List<ObjectGuid> _newObjects = new(Capacity);
    private readonly List<(ObjectGuid Guid, GameEntityFields Fields)> _updatedObjects = new(Capacity);
    private readonly List<ObjectGuid> _removedObjects = new(Capacity);

    // What the last Reset forgot (#611), kept apart from _removedObjects so a reset reports nothing gone.
    private readonly List<ObjectGuid> _forgotten = new(Capacity);

    // What this tick's view holds (#593), reused every tick so a steady tick allocates nothing.
    private readonly List<IWorldObject> _visibleCreatures = new(Capacity);
    private readonly List<IWorldObject> _visibleCharacters = new(Capacity);
    private readonly List<IWorldObject> _visibleWorldObjects = new(Capacity);
    private readonly List<IWorldObject> _visibleDoodads = new(Capacity);

    // This viewer's answer about every doodad with rules it has decided, seen or hidden, so a decision asks a doodad's
    // rules again only when the viewer's quests (_gateVersionSeen, or the catalog they are read against, _questsSeen)
    // or the doodad (DoodadAnswer.ViewVersion) moved. When the quests move, the answers are put aside (_answersAside)
    // for the decision that asks again, which compares against them, and the ones it does not reach are dropped. The two
    // trade places, so neither allocates once grown.
    private Dictionary<ulong, DoodadAnswer> _doodadAnswers = [];
    private Dictionary<ulong, DoodadAnswer> _answersAside = [];
    private int _gateVersionSeen;
    private QuestCatalog? _questsSeen;

    // The doodads in view whose answer to "can I use it" changed in this decision, each owed one update with it.
    private readonly List<ObjectGuid> _canInteractOwed = [];

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

        _doodadTrackingSystem = new EntityTrackingSystem(Capacity);
        _doodadTrackingSystem.EntityAdded += OnEntityFound;
        _doodadTrackingSystem.EntityUpdated += OnDoodadUpdated;
        _doodadTrackingSystem.EntityRemoved += OnEntityRemoved;
    }

    /// <summary>
    /// Diffs this tick's objects against what the client already sees. Only the objects in its view
    /// count (#593): the watcher's own character always, every other one by <paramref name="range" />
    /// from <paramref name="watcherPosition" />, on X/Z. The doodads of <paramref name="doodads" /> count by the same
    /// range and by their rules for its viewer (<see cref="AddVisibleDoodads" />); the default lists none.
    /// </summary>
    public void Update(
        ObjectGuid watcher,
        Vector3 watcherPosition,
        InterestRange range,
        Dictionary<ObjectGuid, ICreature> creatures,
        Dictionary<ObjectGuid, ICharacter> characters,
        List<IWorldObject> worldObjects,
        IReadOnlyDictionary<ObjectGuid, GameEntityFields> frameDirtyFields,
        in DoodadViewInputs doodads = default)
    {
        InstanceTickProfile profile = default;
        UpdateProfiled(watcher, watcherPosition, range, creatures, characters, worldObjects, doodads, frameDirtyFields,
            ref profile);
    }

    internal void UpdateProfiled(
        ObjectGuid watcher,
        Vector3 watcherPosition,
        InterestRange range,
        Dictionary<ObjectGuid, ICreature> creatures,
        Dictionary<ObjectGuid, ICharacter> characters,
        List<IWorldObject> worldObjects,
        in DoodadViewInputs doodads,
        IReadOnlyDictionary<ObjectGuid, GameEntityFields> frameDirtyFields, ref InstanceTickProfile profile)
    {
        long profileStart = profile.Timestamp();
        BeginDecision();

        foreach (ICreature creature in creatures.Values)
        {
            if (InView(creature, _creatureTrackingSystem, watcher, watcherPosition, range))
                _visibleCreatures.Add(creature);
        }

        foreach (ICharacter character in characters.Values)
        {
            if (InView(character, _characterTrackingSystem, watcher, watcherPosition, range))
                _visibleCharacters.Add(character);
        }

        AddVisibleWorldObjects(watcher, watcherPosition, range, worldObjects);
        int doodadsVisited = AddVisibleDoodads(watcher, watcherPosition, range, doodads);
        CompleteDecision((long)creatures.Count + characters.Count + worldObjects.Count + doodadsVisited, frameDirtyFields,
            profileStart, ref profile);
    }

    /// <summary>
    /// <see cref="UpdateProfiled(ObjectGuid, Vector3, InterestRange, Dictionary{ObjectGuid, ICreature}, Dictionary{ObjectGuid, ICharacter}, List{IWorldObject}, in DoodadViewInputs, IReadOnlyDictionary{ObjectGuid, GameEntityFields}, ref InstanceTickProfile)" />
    /// over the instance's spatial index (#982): the candidates are the characters and creatures of the cells within
    /// reach of the watcher (<see cref="Interest.Reach" />, radius + margin), the watcher's own character wherever it
    /// stands, and every world object; each is then decided by <see cref="Interest.IsVisible{TTracked}" /> as the full
    /// scan decides it, a character or creature on the index's own copy of its guid and X/Z, without reading it, unless
    /// it is in the margin (<see cref="ViewCandidates" />). The same objects come
    /// into, stay in and leave the view; within a kind, they are listed in the cells' order rather than the
    /// dictionaries'. <paramref name="watcherPosition" /> is the watcher's own position, as the index holds it.
    /// </summary>
    internal void UpdateProfiled(
        ObjectGuid watcher,
        Vector3 watcherPosition,
        InterestRange range,
        SpatialGrid members,
        List<IWorldObject> worldObjects,
        in DoodadViewInputs doodads,
        IReadOnlyDictionary<ObjectGuid, GameEntityFields> frameDirtyFields, ref InstanceTickProfile profile)
    {
        long profileStart = profile.Timestamp();
        BeginDecision();

        var candidates = new ViewCandidates(this, watcher, watcherPosition, range);
        members.Visit(watcherPosition.x, watcherPosition.z, Interest.Reach(range), ref candidates);
        LastCandidates = candidates.Visited;

        AddVisibleWorldObjects(watcher, watcherPosition, range, worldObjects);
        int doodadsVisited = AddVisibleDoodads(watcher, watcherPosition, range, doodads);
        CompleteDecision((long)candidates.Visited + worldObjects.Count + doodadsVisited, frameDirtyFields, profileStart,
            ref profile);
    }

    /// <summary>
    /// How many characters and creatures the last decision over the spatial index visited, the watcher's own character
    /// included (#982): what the index left of the instance's members to check. For measuring it alone.
    /// </summary>
    internal int LastCandidates { get; private set; }

    private void BeginDecision()
    {
        _newObjects.Clear();
        _updatedObjects.Clear();
        _removedObjects.Clear();
        _visibleCreatures.Clear();
        _visibleCharacters.Clear();
        _visibleWorldObjects.Clear();
        _visibleDoodads.Clear();
        _canInteractOwed.Clear();
    }

    private void AddVisibleWorldObjects(ObjectGuid watcher, Vector3 watcherPosition, InterestRange range,
        List<IWorldObject> worldObjects)
    {
        foreach (IWorldObject worldObject in worldObjects)
        {
            if (InView(worldObject, _worldObjectTrackingSystem, watcher, watcherPosition, range))
                _visibleWorldObjects.Add(worldObject);
        }
    }

    private void CompleteDecision(long candidateChecks, IReadOnlyDictionary<ObjectGuid, GameEntityFields> frameDirtyFields,
        long profileStart, ref InstanceTickProfile profile)
    {
        profile.VisibilityTicks += profile.Elapsed(profileStart);
        if (profile.Enabled)
        {
            profile.CandidateChecks += candidateChecks;
            profile.VisiblePairs += (long)_visibleCreatures.Count + _visibleCharacters.Count + _visibleWorldObjects.Count
                                    + _visibleDoodads.Count;
        }
        profileStart = profile.Timestamp();
        _creatureTrackingSystem.Update(_visibleCreatures, frameDirtyFields);
        _characterTrackingSystem.Update(_visibleCharacters, frameDirtyFields);
        _worldObjectTrackingSystem.Update(_visibleWorldObjects, frameDirtyFields);
        _doodadTrackingSystem.Update(_visibleDoodads, frameDirtyFields);

        // The answers the frame's marks did not carry (OnDoodadUpdated): the doodad's state stood still, the answer moved.
        for (int i = 0; i < _canInteractOwed.Count; i++)
            _updatedObjects.Add((_canInteractOwed[i], GameEntityFields.CanInteract));
        _canInteractOwed.Clear();
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
        _doodadTrackingSystem.Forget(_forgotten);
        _doodadAnswers.Clear();
        _answersAside.Clear();
        _canInteractOwed.Clear();
        ScanOwed = true;
        return _forgotten;
    }

    /// <summary>
    /// Whether the client has been told of this character, creature or doodad and not told it is gone, as of the last
    /// decision of its view (auras): what it holds, which since #981 can be up to a broadcast period behind what is in
    /// range.
    /// </summary>
    public bool Knows(ObjectGuid guid) => _characterTrackingSystem.IsTracked(guid) || _creatureTrackingSystem.IsTracked(guid)
                                          || _doodadTrackingSystem.IsTracked(guid);

    /// <summary>
    /// Whether this viewer's client is to be offered the use of <paramref name="doodad" />, as of this tick's decision of
    /// its view: for a doodad with rules, the answer that decision holds, and no when it holds none for the doodad as it
    /// is now, failing closed; for any other, what its state allows (<see cref="DoodadVisibility.Usable" />). What its
    /// add and updates carry, so asked only of a doodad in this tick's view.
    /// </summary>
    internal bool CanInteractWith(Doodad doodad)
    {
        if (!DoodadVisibility.AsksRules(doodad))
            return DoodadVisibility.Usable(doodad);

        return _doodadAnswers.TryGetValue(doodad.Guid.RawValue, out DoodadAnswer answer)
               && answer.ViewVersion == doodad.ViewVersion && answer.CanInteract;
    }

    /// <summary>
    /// Lists the doodads in view: the doodads of the index's cells within reach of the watcher, each decided by the
    /// interest rule creatures are (<see cref="Interest.IsVisible{TTracked}" />, the doodad tracker as what the client
    /// holds), and one with rules (<see cref="DoodadVisibility.AsksRules" />) by its rules for this viewer too. Those
    /// rules are asked only on the first decision that finds the doodad in range, on one after the viewer's quests moved
    /// (<see cref="QuestLog.GateVersion" />, or the quest catalog was reloaded), and on one after the doodad did
    /// (<see cref="Doodad.ViewVersion" />); every other decision reuses the answer held, so a steady view asks nothing
    /// and allocates nothing. Returns how many doodads the index visited.
    /// </summary>
    private int AddVisibleDoodads(ObjectGuid watcher, Vector3 watcherPosition, InterestRange range,
        in DoodadViewInputs doodads)
    {
        if (doodads.Index is not { Count: > 0 } index || doodads.Viewer is not { } viewer
            || doodads.Quests is not { } quests || doodads.Visibility is not { } visibility)
        {
            return 0;
        }

        // A /reload quests is a move too: an objective's stage or count may be another in the new catalog.
        int gateVersion = viewer.Quests.GateVersion;
        bool gatesMoved = gateVersion != _gateVersionSeen || !ReferenceEquals(quests, _questsSeen);
        if (gatesMoved)
        {
            // Every answer held was decided on quests that have moved since. Those within reach are asked again below,
            // against the ones put aside; the rest are dropped, and asked when they come within reach again.
            (_answersAside, _doodadAnswers) = (_doodadAnswers, _answersAside);
            _doodadAnswers.Clear();
            _gateVersionSeen = gateVersion;
            _questsSeen = quests;
        }

        var candidates = new DoodadCandidates(this, watcher, watcherPosition, range, viewer, quests, visibility,
            gatesMoved);
        index.Visit(watcherPosition.x, watcherPosition.z, Interest.Reach(range), ref candidates);

        if (gatesMoved)
            _answersAside.Clear();
        return candidates.Visited;
    }

    /// <summary>
    /// Whether this viewer sees <paramref name="doodad" />, a doodad with rules in range: the answer held when neither the
    /// viewer's quests nor the doodad moved since it was decided, else its rules asked again and the new answer held. A
    /// doodad the client holds and goes on holding whose "can I use it" answer changed is owed an update with it.
    /// </summary>
    private bool Sees(Doodad doodad, CharacterEntity viewer, QuestCatalog quests, DoodadVisibility visibility,
        bool gatesMoved)
    {
        ulong raw = doodad.Guid.RawValue;
        ref DoodadAnswer held = ref CollectionsMarshal.GetValueRefOrAddDefault(_doodadAnswers, raw, out bool decided);
        if (decided && held.ViewVersion == doodad.ViewVersion)
            return held.Visible;

        // The answer the client was given last: the one held when only the doodad moved, the one put aside when the
        // viewer's quests did, none when the doodad is new to this viewer.
        DoodadAnswer before = held;
        bool known = decided || (gatesMoved && _answersAside.TryGetValue(raw, out before));

        // Answer never throws (a script's CanSee is contained in it, the gate's answer standing), so the entry added above
        // always ends up holding an answer at the doodad's current version, and the answers put aside stay whole.
        held = visibility.Answer(doodad, viewer, quests);
        if (held.Visible && known && before.Visible && before.CanInteract != held.CanInteract
            && _doodadTrackingSystem.IsTracked(doodad.Guid))
        {
            _canInteractOwed.Add(doodad.Guid);
        }

        return held.Visible;
    }

    // "Already tracked" is the object's own tracking system's set, so a tracked object keeps its margin. The set is
    // asked only for an object in the margin (#982): within the radius or beyond the margin it cannot change the answer.
    private static bool InView(IWorldObject obj, EntityTrackingSystem tracking, ObjectGuid watcher,
        Vector3 watcherPosition, InterestRange range) =>
        Interest.IsVisible(watcher, watcherPosition, obj.Guid, obj.Position, range, new TrackedBy(tracking));

    private readonly struct TrackedBy(EntityTrackingSystem tracking) : Interest.ITrackedSet
    {
        public bool Contains(ObjectGuid obj) => tracking.IsTracked(obj);
    }

    /// <summary>
    /// The spatial index's candidates for one view (#982), each decided as <see cref="Interest.IsVisible{TTracked}" />
    /// decides it on the entity, and listed with its kind. An entry of a cell is decided on the index's copy of its guid
    /// and X/Z, without reading the entity (a character's position is on its row, a second object): the watcher's own
    /// character is in view; with a watcher whose position is not finite, nothing else is; otherwise the distance
    /// decides, the same measure on the same floats, unless the entry is in the margin, which asks the entity's
    /// tracking. An entry in no cell or not indexed is decided on the entity, as the full scan decides it.
    /// </summary>
    private struct ViewCandidates : ISpatialVisitor
    {
        private readonly CharacterCharacterGameState _state;
        private readonly ObjectGuid _watcher;
        private readonly ulong _watcherRaw;
        private readonly Vector3 _watcherPosition;
        private readonly bool _watcherFinite;
        private readonly InterestRange _range;

        public int Visited;

        public ViewCandidates(CharacterCharacterGameState state, ObjectGuid watcher, Vector3 watcherPosition,
            InterestRange range)
        {
            _state = state;
            _watcher = watcher;
            _watcherRaw = watcher.RawValue;
            _watcherPosition = watcherPosition;
            _watcherFinite = EffectAudience.IsFinite(watcherPosition);
            _range = range;
        }

        // A cell's entry: every component of its position is finite and its X/Z are the entity's (SpatialGrid), so the
        // rule's own steps are taken on the copy, in its order: self, a watcher not finite, then the distance.
        public void VisitPlaced(SpatialKind kind, ulong rawGuid, float x, float z, IWorldObject obj)
        {
            Visited++;
            if (rawGuid == _watcherRaw)
            {
                Listed(kind, obj);
                return;
            }

            if (!_watcherFinite)
                return;

            if (Interest.DecidedByDistance(_watcherPosition, x, z, _range, out bool inView))
            {
                if (inView)
                    Listed(kind, obj);
                return;
            }

            Decide(kind, obj);
        }

        public void Visit(SpatialKind kind, ulong rawGuid, float x, float z, IWorldObject obj)
        {
            Visited++;
            Decide(kind, obj);
        }

        // On the entity, as the full scan decides it.
        private readonly void Decide(SpatialKind kind, IWorldObject obj)
        {
            EntityTrackingSystem tracking = kind == SpatialKind.Creature
                ? _state._creatureTrackingSystem
                : _state._characterTrackingSystem;
            if (InView(obj, tracking, _watcher, _watcherPosition, _range))
                Listed(kind, obj);
        }

        private readonly void Listed(SpatialKind kind, IWorldObject obj)
        {
            if (kind == SpatialKind.Creature)
                _state._visibleCreatures.Add(obj);
            else
                _state._visibleCharacters.Add(obj);
        }
    }

    /// <summary>
    /// The index's doodads within reach of one view, each decided by the interest rule on the doodad itself (a doodad
    /// never moves, so its position is the index's) and, when it has rules, by them (<see cref="Sees" />). A despawned
    /// doodad is in no view.
    /// </summary>
    private struct DoodadCandidates : IDoodadVisitor
    {
        private readonly CharacterCharacterGameState _state;
        private readonly ObjectGuid _watcher;
        private readonly Vector3 _watcherPosition;
        private readonly InterestRange _range;
        private readonly CharacterEntity _viewer;
        private readonly QuestCatalog _quests;
        private readonly DoodadVisibility _visibility;
        private readonly bool _gatesMoved;

        public int Visited;

        public DoodadCandidates(CharacterCharacterGameState state, ObjectGuid watcher, Vector3 watcherPosition,
            InterestRange range, CharacterEntity viewer, QuestCatalog quests, DoodadVisibility visibility, bool gatesMoved)
        {
            _state = state;
            _watcher = watcher;
            _watcherPosition = watcherPosition;
            _range = range;
            _viewer = viewer;
            _quests = quests;
            _visibility = visibility;
            _gatesMoved = gatesMoved;
        }

        public void Visit(Doodad doodad)
        {
            Visited++;
            if (!doodad.Visible
                || !InView(doodad, _state._doodadTrackingSystem, _watcher, _watcherPosition, _range)
                || (DoodadVisibility.AsksRules(doodad) && !_state.Sees(doodad, _viewer, _quests, _visibility, _gatesMoved)))
            {
                return;
            }

            _state._visibleDoodads.Add(doodad);
        }
    }

    // A doodad's state marked this frame: one entry carries the mark and, when this decision changed it, the viewer's
    // answer to "can I use it", so a doodad is updated at most once per decision.
    private void OnDoodadUpdated(ObjectGuid guid, GameEntityFields fields)
    {
        for (int i = 0; i < _canInteractOwed.Count; i++)
        {
            if (_canInteractOwed[i].RawValue != guid.RawValue)
                continue;

            fields |= GameEntityFields.CanInteract;
            _canInteractOwed.RemoveAt(i);
            break;
        }

        _updatedObjects.Add((guid, fields));
    }

    private void OnEntityRemoved(ObjectGuid guid) => _removedObjects.Add(guid);
    private void OnEntityUpdated(ObjectGuid guid, GameEntityFields fields) => _updatedObjects.Add((guid, fields));
    private void OnEntityFound(ObjectGuid guid) => _newObjects.Add(guid);
}
