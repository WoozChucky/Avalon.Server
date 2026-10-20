using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;

namespace Avalon.World.Chat;

/// <summary>
/// The creatures <c>/spawn</c> placed, by instance, so <c>/despawn</c> removes those and never an item's summon or a
/// map's own creatures. Tick thread only, as both commands are.
/// </summary>
public sealed class GmSpawns
{
    private readonly Dictionary<Guid, List<ICreature>> _byInstance = [];

    public void Add(Guid instanceId, ICreature creature)
    {
        if (!_byInstance.TryGetValue(instanceId, out List<ICreature>? creatures))
        {
            creatures = [];
            _byInstance[instanceId] = creatures;
        }

        creatures.Add(creature);
    }

    /// <summary>The instance's spawns, forgotten here; empty when there are none.</summary>
    public List<ICreature> Take(Guid instanceId) =>
        _byInstance.Remove(instanceId, out List<ICreature>? creatures) ? creatures : [];

    /// <summary>
    /// Forgets every spawn no longer standing: dead, gone from its instance (expired or removed), or in an instance that
    /// no longer exists, so nothing departed is held or counted.
    /// </summary>
    public void ForgetGone(IInstanceRegistry instances)
    {
        foreach (Guid instanceId in _byInstance.Keys.ToList())
        {
            List<ICreature> creatures = _byInstance[instanceId];
            if (instances.GetInstanceById(instanceId) is { } instance)
                creatures.RemoveAll(c => c.CurrentHealth == 0 || !instance.Creatures.ContainsKey(c.Guid));
            else
                creatures.Clear();
            if (creatures.Count == 0)
                _byInstance.Remove(instanceId);
        }
    }
}
