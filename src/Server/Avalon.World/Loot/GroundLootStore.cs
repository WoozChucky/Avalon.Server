using System.Diagnostics.CodeAnalysis;
using Avalon.Common;

namespace Avalon.World.Loot;

/// <summary>The drops on the ground in one instance. Tick thread only, like the rest of the instance.</summary>
public sealed class GroundLootStore
{
    // Keyed by the raw value, not the ObjectGuid: ObjectGuid is a mutable class, and a Set on the
    // object used as a key would strand its entry.
    private readonly Dictionary<ulong, GroundLoot> _drops = [];

    public int Count => _drops.Count;

    public IReadOnlyCollection<GroundLoot> All => _drops.Values;

    public void Add(GroundLoot drop) => _drops.Add(drop.Guid.RawValue, drop);

    public bool TryGet(ObjectGuid guid, [NotNullWhen(true)] out GroundLoot? drop) => _drops.TryGetValue(guid.RawValue, out drop);

    public bool Remove(ObjectGuid guid) => _drops.Remove(guid.RawValue);

    public void Clear() => _drops.Clear();
}
