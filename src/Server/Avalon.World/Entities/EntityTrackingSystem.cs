using Avalon.Common;
using Avalon.World.Public;
using Avalon.World.Public.Enums;

namespace Avalon.World.Entities;

public class EntityTrackingSystem(int capacity)
{
    private readonly HashSet<ObjectGuid> _trackedGuids = new(capacity);
    private readonly HashSet<ObjectGuid> _seenThisFrame = new(capacity);
    private readonly List<ObjectGuid> _pendingRemovals = new(capacity);

    public event Action<ObjectGuid>? EntityAdded;
    public event Action<ObjectGuid>? EntityRemoved;
    public event Action<ObjectGuid, GameEntityFields>? EntityUpdated;

    /// <summary>Whether <paramref name="guid" /> is in this system's set, as of its last update.</summary>
    public bool IsTracked(ObjectGuid guid) => _trackedGuids.Contains(guid);

    /// <summary>
    /// Empties the set without raising <see cref="EntityRemoved" /> (#611), appending every guid it held
    /// to <paramref name="forgotten" />, so the next update adds whatever is in view as new.
    /// </summary>
    public void Forget(List<ObjectGuid> forgotten)
    {
        forgotten.AddRange(_trackedGuids);
        _trackedGuids.Clear();
    }

    public void Update(
        List<IWorldObject> currentEntities,
        IReadOnlyDictionary<ObjectGuid, GameEntityFields> frameDirtyFields)
    {
        _seenThisFrame.Clear();

        foreach (var entity in currentEntities)
        {
            _seenThisFrame.Add(entity.Guid);

            if (!_trackedGuids.Contains(entity.Guid))
            {
                _trackedGuids.Add(entity.Guid);
                EntityAdded?.Invoke(entity.Guid);
                continue;
            }

            if (frameDirtyFields.TryGetValue(entity.Guid, out var dirtyFields))
            {
                EntityUpdated?.Invoke(entity.Guid, dirtyFields);
            }
        }

        foreach (var guid in _trackedGuids)
        {
            if (!_seenThisFrame.Contains(guid))
                _pendingRemovals.Add(guid);
        }

        foreach (var guid in _pendingRemovals)
        {
            _trackedGuids.Remove(guid);
            EntityRemoved?.Invoke(guid);
        }

        _pendingRemovals.Clear();
    }
}
