using System.Runtime.InteropServices;
using Avalon.Common;
using Avalon.World.Public;
using Avalon.World.Public.Enums;

namespace Avalon.World.Entities;

public class EntityTrackingSystem(int capacity)
{
    // The wire identity is stable while tracked. Retain its wrapper for notifications without
    // paying ObjectGuid's virtual hash/equality or rebuilding a second set on every frame.
    private readonly Dictionary<ulong, TrackedEntity> _tracked = new(capacity);
    private readonly List<ObjectGuid> _pendingRemovals = new(capacity);
    private uint _frame;

    public event Action<ObjectGuid>? EntityAdded;
    public event Action<ObjectGuid>? EntityRemoved;
    public event Action<ObjectGuid, GameEntityFields>? EntityUpdated;

    /// <summary>Whether <paramref name="guid" /> is in this system's set, as of its last update.</summary>
    public bool IsTracked(ObjectGuid guid) => _tracked.ContainsKey(guid.RawValue);

    /// <summary>
    /// Takes <paramref name="guid" /> out of the set without raising <see cref="EntityRemoved" />: for an entity known to
    /// have gone, whose removal the caller reports itself (#981). False when it was not in the set.
    /// </summary>
    public bool Untrack(ObjectGuid guid) => _tracked.Remove(guid.RawValue);

    /// <summary>
    /// Empties the set without raising <see cref="EntityRemoved" /> (#611), appending every guid it held
    /// to <paramref name="forgotten" />, so the next update adds whatever is in view as new.
    /// </summary>
    public void Forget(List<ObjectGuid> forgotten)
    {
        foreach (KeyValuePair<ulong, TrackedEntity> entry in _tracked)
            forgotten.Add(entry.Value.Guid);
        _tracked.Clear();
    }

    public void Update(
        List<IWorldObject> currentEntities,
        IReadOnlyDictionary<ObjectGuid, GameEntityFields> frameDirtyFields)
    {
        _frame = unchecked(_frame + 1);
        if (_frame == 0)
        {
            // Rebase on wrap without losing membership. Replacing dictionary values does not
            // invalidate its enumerator; no keys are added or removed in this pass.
            foreach (KeyValuePair<ulong, TrackedEntity> entry in _tracked)
                CollectionsMarshal.GetValueRefOrNullRef(_tracked, entry.Key).SeenFrame = 0;
            _frame = 1;
        }

        int seen = 0;
        bool hasDirtyFields = frameDirtyFields.Count != 0;
        foreach (IWorldObject entity in currentEntities)
        {
            ObjectGuid guid = entity.Guid;
            ref TrackedEntity tracked = ref CollectionsMarshal.GetValueRefOrAddDefault(_tracked, guid.RawValue, out bool exists);
            if (!exists)
            {
                tracked = new TrackedEntity(guid, _frame);
                seen++;
                EntityAdded?.Invoke(guid);
                continue;
            }

            // Count unique guids, but preserve per-input dirty notifications for duplicate inputs.
            if (tracked.SeenFrame != _frame)
            {
                tracked.SeenFrame = _frame;
                seen++;
            }

            // Do not use the dictionary ref after a callback, which may change membership.
            if (hasDirtyFields && frameDirtyFields.TryGetValue(guid, out GameEntityFields dirtyFields))
                EntityUpdated?.Invoke(guid, dirtyFields);
        }

        // Every seen guid is tracked. Equal counts mean no removals; pending callbacks still need
        // replay after an interrupted update, even if the live membership already matches.
        if (seen == _tracked.Count && _pendingRemovals.Count == 0)
            return;

        foreach (KeyValuePair<ulong, TrackedEntity> entry in _tracked)
        {
            if (entry.Value.SeenFrame != _frame)
                _pendingRemovals.Add(entry.Value.Guid);
        }

        foreach (ObjectGuid guid in _pendingRemovals)
        {
            _tracked.Remove(guid.RawValue);
            EntityRemoved?.Invoke(guid);
        }

        _pendingRemovals.Clear();
    }

    private struct TrackedEntity(ObjectGuid guid, uint seenFrame)
    {
        public ObjectGuid Guid = guid;
        public uint SeenFrame = seenFrame;
    }
}
