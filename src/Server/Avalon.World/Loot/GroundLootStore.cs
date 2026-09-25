using System.Diagnostics.CodeAnalysis;
using Avalon.Common;

namespace Avalon.World.Loot;

/// <summary>The drops on the ground in one instance. Tick thread only, like the rest of the instance.</summary>
public sealed class GroundLootStore
{
    private readonly Dictionary<ObjectGuid, GroundLoot> _drops = [];

    public int Count => _drops.Count;

    public IReadOnlyCollection<GroundLoot> All => _drops.Values;

    public void Add(GroundLoot drop) => _drops.Add(drop.Guid, drop);

    public bool TryGet(ObjectGuid guid, [NotNullWhen(true)] out GroundLoot? drop) => _drops.TryGetValue(guid, out drop);

    public bool Remove(ObjectGuid guid) => _drops.Remove(guid);

    public void Clear() => _drops.Clear();
}
