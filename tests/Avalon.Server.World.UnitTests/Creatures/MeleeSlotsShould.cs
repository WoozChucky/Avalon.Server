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

    // Most of these tests care about slot exclusivity/lifecycle, not bearing, so target and
    // claimant share a position — bearing is then a well-defined 0 (Atan2(0, 0)) rather than
    // something the test has to reason about.
    private static readonly Vector3 TargetPosition = Vector3.zero;

    [Fact]
    public void Give_Four_Claimants_Four_Distinct_Slots()
    {
        var slots = new MeleeSlots(slotCount: 6, radius: 1.5f);

        var claimed = new List<int>();
        for (uint i = 1; i <= 4; i++)
        {
            Assert.True(slots.TryClaim(Target, Creature(i), TargetPosition, TargetPosition, out int slot));
            claimed.Add(slot);
        }

        Assert.Equal(4, claimed.Distinct().Count());
    }

    /// <summary>Review Focus 1. More attackers than slots must still chase, not throw.</summary>
    [Fact]
    public void Refuse_A_Claim_Once_Every_Slot_Is_Taken()
    {
        var slots = new MeleeSlots(slotCount: 2, radius: 1.5f);
        slots.TryClaim(Target, Creature(1), TargetPosition, TargetPosition, out _);
        slots.TryClaim(Target, Creature(2), TargetPosition, TargetPosition, out _);

        Assert.False(slots.TryClaim(Target, Creature(3), TargetPosition, TargetPosition, out _));
    }

    /// <summary>Review Focus 4. A dies as B arrives, in one tick.</summary>
    [Fact]
    public void Let_The_Next_Claimant_Take_A_Released_Slot_Immediately()
    {
        var slots = new MeleeSlots(slotCount: 1, radius: 1.5f);
        slots.TryClaim(Target, Creature(1), TargetPosition, TargetPosition, out int first);
        slots.Release(Target, Creature(1));

        Assert.True(slots.TryClaim(Target, Creature(2), TargetPosition, TargetPosition, out int second));
        Assert.Equal(first, second);
    }

    [Fact]
    public void Keep_A_Claimants_Slot_Stable_Across_Repeated_Claims()
    {
        var slots = new MeleeSlots(slotCount: 6, radius: 1.5f);
        slots.TryClaim(Target, Creature(1), TargetPosition, TargetPosition, out int first);

        // A repeat claim from a DIFFERENT position (and so a different bearing) must not move the
        // claimant to a "closer" slot — the slot is fixed at first claim, not re-picked per tick.
        Assert.True(slots.TryClaim(Target, Creature(1), TargetPosition, new Vector3(-5f, 0f, 0f), out int again));
        Assert.Equal(first, again);
    }

    [Fact]
    public void Release_Every_Slot_When_The_Target_Dies()
    {
        var slots = new MeleeSlots(slotCount: 2, radius: 1.5f);
        slots.TryClaim(Target, Creature(1), TargetPosition, TargetPosition, out _);
        slots.TryClaim(Target, Creature(2), TargetPosition, TargetPosition, out _);

        slots.ReleaseTarget(Target);

        Assert.True(slots.TryClaim(Target, Creature(3), TargetPosition, TargetPosition, out _));
        Assert.True(slots.TryClaim(Target, Creature(4), TargetPosition, TargetPosition, out _));
    }

    /// <summary>
    /// TryClaim hands out the free slot nearest the claimant's bearing from the target, not the
    /// lowest free index — otherwise a creature standing right next to a far slot gets sent on a
    /// long tangential walk around the ring instead of taking the near one that's actually free.
    /// </summary>
    [Fact]
    public void Give_A_Claimant_The_Slot_Nearest_Its_Bearing_Rather_Than_The_Lowest_Free_Index()
    {
        var slots = new MeleeSlots(slotCount: 6, radius: 1.5f);

        // Slot 0 sits at bearing 0 (target + (radius, 0, 0)). Claim it first, from directly that
        // bearing, so the lowest free index becomes 1 — the wrong answer this test guards against.
        Assert.True(slots.TryClaim(Target, Creature(1), TargetPosition, new Vector3(1f, 0f, 0f), out int firstSlot));
        Assert.Equal(0, firstSlot);

        // Approaching from directly opposite the target (bearing PI) should claim slot 3, which
        // sits at bearing PI — not slot 1, the lowest still-free index (bearing PI/3).
        Assert.True(slots.TryClaim(Target, Creature(2), TargetPosition, new Vector3(-5f, 0f, 0f), out int secondSlot));
        Assert.Equal(3, secondSlot);
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
    /// Distance-from-centre alone can't catch a wrong angle step: |cos, sin| == 1 regardless of
    /// the angle, so every slot collapsing onto a single point still passes that check, and
    /// distinct slot indices alone don't guarantee distinct positions either. This pins the
    /// actual user-visible requirement — creatures standing in their slots do not overlap — by
    /// requiring every pair of the default 6 slots (at the default 1.5f radius) to be at least
    /// one agent diameter apart: 1.2f, since NavmeshBuildSettings.AgentRadius defaults to 0.6f.
    /// </summary>
    [Fact]
    public void Space_Every_Pair_Of_Slots_At_Least_One_Agent_Diameter_Apart()
    {
        const int slotCount = 6;
        const float agentDiameter = 1.2f; // 2 * NavmeshBuildSettings.AgentRadius (0.6f)

        var slots = new MeleeSlots(slotCount, radius: 1.5f);
        var centre = new Vector3(10f, 5f, 10f);

        var positions = new Vector3[slotCount];
        for (int slot = 0; slot < slotCount; slot++)
        {
            positions[slot] = slots.PositionFor(centre, slot);
        }

        for (int i = 0; i < slotCount; i++)
        {
            for (int j = i + 1; j < slotCount; j++)
            {
                float distance = Vector3.Distance(positions[i], positions[j]);
                Assert.True(distance >= agentDiameter,
                    $"Slots {i} and {j} are only {distance} apart — closer than one agent diameter ({agentDiameter}).");
            }
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
        Assert.True(slots.TryClaim(Target, Creature(1), TargetPosition, TargetPosition, out int first));

        slots.ReleaseClaimant(Creature(1));

        Assert.True(slots.TryClaim(Target, Creature(2), TargetPosition, TargetPosition, out int second));
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
        slots.TryClaim(Target, Creature(1), TargetPosition, TargetPosition, out _);

        slots.Release(Target, Creature(1));

        Assert.False(ClaimsOf(slots).ContainsKey(Target));
    }

    private static Dictionary<ObjectGuid, Dictionary<ObjectGuid, int>> ClaimsOf(MeleeSlots slots)
    {
        FieldInfo field = typeof(MeleeSlots).GetField("_claims", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Dictionary<ObjectGuid, Dictionary<ObjectGuid, int>>)field.GetValue(slots)!;
    }
}
