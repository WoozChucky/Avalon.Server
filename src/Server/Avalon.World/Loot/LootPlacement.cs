using System.Diagnostics;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Public.Maps;

namespace Avalon.World.Loot;

/// <summary>
/// Whom each drop of one kill is reserved for and until when, asked by the drop's index, once per drop and in order.
/// A struct implements it, so a kill's placement is handed its allocations without a closure or a delegate (#890).
/// </summary>
public interface IDropAllocations
{
    LootAllocation For(int index);
}

/// <summary>Puts one kill's drops on the ground around the corpse.</summary>
public static class LootPlacement
{
    /// <summary>Metres from the corpse. Close enough to read as "its drops", far enough apart to click.</summary>
    public const float RingRadius = 1f;

    /// <summary>
    /// Adds the placed drops to <paramref name="placed" />, an empty list the caller keeps, so that a kill allocates
    /// only the drops themselves (#890). Drop <c>i</c> of <c>n</c> goes at angle 2*pi*i/n on the ring, pulled back to
    /// the last walkable point between the corpse and there, then snapped to the navmesh with the corpse's height as
    /// the search centre: the same snap authored creature spawns get. Each drop's owner and free-for-all time are
    /// asked of <paramref name="allocations" /> by the drop's index, once per drop and in order: a party kill reserves
    /// each drop for its own member.
    /// </summary>
    public static void Place<TAllocations>(
        List<GroundLoot> placed,
        Vector3 corpse,
        IReadOnlyList<RolledDrop> rolled,
        TAllocations allocations,
        IMapNavigator navigator,
        Func<uint> nextId)
        where TAllocations : struct, IDropAllocations
    {
        Debug.Assert(placed.Count == 0, "The list of placed drops still holds an earlier kill's");

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
            LootAllocation allocation = allocations.For(i);
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
    }
}
