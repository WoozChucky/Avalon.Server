using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Public.Maps;

namespace Avalon.World.Loot;

/// <summary>Puts one kill's drops on the ground around the corpse.</summary>
public static class LootPlacement
{
    /// <summary>Metres from the corpse. Close enough to read as "its drops", far enough apart to click.</summary>
    public const float RingRadius = 1f;

    /// <summary>
    /// Drop <c>i</c> of <c>n</c> at angle 2*pi*i/n on the ring, pulled back to the last walkable point
    /// between the corpse and there, then snapped to the navmesh with the corpse's height as the
    /// search centre: the same snap authored creature spawns get.
    /// </summary>
    public static IReadOnlyList<GroundLoot> Place(
        Vector3 corpse,
        IReadOnlyList<RolledDrop> rolled,
        LootAllocation allocation,
        IMapNavigator navigator,
        Func<uint> nextId)
    {
        var placed = new List<GroundLoot>(rolled.Count);

        for (int i = 0; i < rolled.Count; i++)
        {
            float angle = 2f * MathF.PI * i / rolled.Count;
            var ringPoint = new Vector3(
                corpse.x + MathF.Cos(angle) * RingRadius,
                corpse.y,
                corpse.z + MathF.Sin(angle) * RingRadius);

            // Without this a corpse against a wall puts drops inside it, where no one can reach them.
            Vector3 reachable = navigator.RaycastWalkable(corpse, ringPoint);
            float groundY = navigator.SampleGroundHeight(reachable.x, corpse.y, reachable.z);

            RolledDrop drop = rolled[i];
            placed.Add(new GroundLoot
            {
                Guid = new ObjectGuid(ObjectType.Loot, nextId()),
                Position = new Vector3(reachable.x, groundY, reachable.z),
                ItemTemplateId = drop.ItemTemplateId,
                Count = drop.Count,
                Gold = drop.Gold,
                OwnerCharacterId = allocation.OwnerCharacterId,
                FreeForAllAt = allocation.FreeForAllAt,
            });
        }

        return placed;
    }
}
