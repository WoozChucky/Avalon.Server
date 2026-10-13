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
    /// Drop <c>i</c> of <c>n</c> at angle 2*pi*i/n on the ring, pulled back to the last walkable point
    /// between the corpse and there, then snapped to the navmesh with the corpse's height as the
    /// search centre: the same snap authored creature spawns get.
    /// </summary>
    public static IReadOnlyList<GroundLoot> Place(
        Vector3 corpse,
        IReadOnlyList<RolledDrop> rolled,
        LootAllocation allocation,
        IMapNavigator navigator,
        Func<uint> nextId) =>
        Place(corpse, rolled, _ => allocation, navigator, nextId);

    /// <summary>
    /// The same placement, with each drop's owner and free-for-all time asked of <paramref name="allocationFor" />
    /// by the drop's index, once per drop and in order: a party kill reserves each drop for its own member.
    /// </summary>
    public static IReadOnlyList<GroundLoot> Place(
        Vector3 corpse,
        IReadOnlyList<RolledDrop> rolled,
        Func<int, LootAllocation> allocationFor,
        IMapNavigator navigator,
        Func<uint> nextId)
    {
        var placed = new List<GroundLoot>(rolled.Count);
        Place(placed, corpse, rolled, new ByDelegate(allocationFor), navigator, nextId);
        return placed;
    }

    /// <summary>
    /// The same placement added to <paramref name="placed" />, a list the caller keeps, with the allocations asked of
    /// a struct: what a kill uses, so that it allocates only the drops themselves (#890).
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

    private readonly struct ByDelegate(Func<int, LootAllocation> allocationFor) : IDropAllocations
    {
        public LootAllocation For(int index) => allocationFor(index);
    }
}
