using System.Reflection;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Creatures;
using Xunit;

namespace Avalon.Server.World.UnitTests.Creatures;

/// <summary>
/// Melee slots hand out distinct standing positions on a ring around a target, keyed by target so two
/// creatures choosing independently never land on the same spot, and released reliably so a dead or
/// retargeted creature never leaves a slot permanently claimed.
/// </summary>
public class MeleeSlotsShould
{
    private static readonly ObjectGuid Target = new(ObjectType.Character, 1);
    private static ObjectGuid Creature(uint id) => new(ObjectType.Creature, id);

    [Fact]
    public void Give_Four_Claimants_Four_Distinct_Slots()
    {
        var slots = new MeleeSlots(slotCount: 6, radius: 1.5f);

        var claimed = new List<int>();
        for (uint i = 1; i <= 4; i++)
        {
            Assert.True(slots.TryClaim(Target, Creature(i), out int slot));
            claimed.Add(slot);
        }

        Assert.Equal(4, claimed.Distinct().Count());
    }

    /// <summary>Review Focus 1. More attackers than slots must still chase, not throw.</summary>
    [Fact]
    public void Refuse_A_Claim_Once_Every_Slot_Is_Taken()
    {
        var slots = new MeleeSlots(slotCount: 2, radius: 1.5f);
        slots.TryClaim(Target, Creature(1), out _);
        slots.TryClaim(Target, Creature(2), out _);

        Assert.False(slots.TryClaim(Target, Creature(3), out _));
    }

    /// <summary>Review Focus 4. A dies as B arrives, in one tick.</summary>
    [Fact]
    public void Let_The_Next_Claimant_Take_A_Released_Slot_Immediately()
    {
        var slots = new MeleeSlots(slotCount: 1, radius: 1.5f);
        slots.TryClaim(Target, Creature(1), out int first);
        slots.Release(Target, Creature(1));

        Assert.True(slots.TryClaim(Target, Creature(2), out int second));
        Assert.Equal(first, second);
    }

    [Fact]
    public void Keep_A_Claimants_Slot_Stable_Across_Repeated_Claims()
    {
        var slots = new MeleeSlots(slotCount: 6, radius: 1.5f);
        slots.TryClaim(Target, Creature(1), out int first);

        Assert.True(slots.TryClaim(Target, Creature(1), out int again));
        Assert.Equal(first, again);
    }

    [Fact]
    public void Release_Every_Slot_When_The_Target_Dies()
    {
        var slots = new MeleeSlots(slotCount: 2, radius: 1.5f);
        slots.TryClaim(Target, Creature(1), out _);
        slots.TryClaim(Target, Creature(2), out _);

        slots.ReleaseTarget(Target);

        Assert.True(slots.TryClaim(Target, Creature(3), out _));
        Assert.True(slots.TryClaim(Target, Creature(4), out _));
    }

    [Fact]
    public void Place_Slots_On_A_Ring_Of_The_Configured_Radius()
    {
        var slots = new MeleeSlots(slotCount: 4, radius: 2f);
        var centre = new Vector3(10f, 5f, 10f);

        for (int slot = 0; slot < 4; slot++)
        {
            Vector3 position = slots.PositionFor(centre, slot);
            Assert.InRange(Vector3.Distance(centre, position), 1.99f, 2.01f);
            Assert.Equal(centre.y, position.y); // ring is horizontal
        }
    }

    /// <summary>
    /// A creature removed on a path that never runs the combat script's own release (e.g. despawn)
    /// has only the target's guid it can no longer be trusted to still know. Releasing by claimant
    /// alone must free the same slot regardless.
    /// </summary>
    [Fact]
    public void Release_A_Claimants_Slot_Without_Naming_The_Target()
    {
        var slots = new MeleeSlots(slotCount: 1, radius: 1.5f);
        Assert.True(slots.TryClaim(Target, Creature(1), out int first));

        slots.ReleaseClaimant(Creature(1));

        Assert.True(slots.TryClaim(Target, Creature(2), out int second));
        Assert.Equal(first, second);
    }

    /// <summary>
    /// <see cref="MeleeSlots.Release"/> must drop the now-empty per-target dictionary, not just empty
    /// it out — otherwise every target ever attacked leaves a permanent entry behind for the life of
    /// the instance. There is no public way to observe this (every public method behaves identically
    /// whether the entry is pruned or merely empty), so this reaches into the private `_claims` field
    /// via reflection rather than asserting through behaviour.
    /// </summary>
    [Fact]
    public void Prune_The_Targets_Entry_Once_Its_Last_Claimant_Is_Released()
    {
        var slots = new MeleeSlots(slotCount: 2, radius: 1.5f);
        slots.TryClaim(Target, Creature(1), out _);

        slots.Release(Target, Creature(1));

        Assert.False(ClaimsOf(slots).ContainsKey(Target));
    }

    private static Dictionary<ObjectGuid, Dictionary<ObjectGuid, int>> ClaimsOf(MeleeSlots slots)
    {
        FieldInfo field = typeof(MeleeSlots).GetField("_claims", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Dictionary<ObjectGuid, Dictionary<ObjectGuid, int>>)field.GetValue(slots)!;
    }
}
