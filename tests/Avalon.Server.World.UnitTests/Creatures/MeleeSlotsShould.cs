using System.Reflection;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Creatures;

namespace Avalon.Server.World.UnitTests.Creatures;

/// <summary>
/// Melee slots hand out distinct standing positions on a ring around a target, keyed by target so two
/// creatures choosing independently never land on the same spot, and released reliably so a dead or
/// retargeted creature never leaves a slot permanently claimed.
/// </summary>
public class MeleeSlotsShould
{
    private static readonly ObjectGuid s_target = new(ObjectType.Character, 1);
    private static ObjectGuid Creature(uint id) => new(ObjectType.Creature, id);

    // Most of these tests care about slot exclusivity/lifecycle, not bearing, so target and
    // claimant share a position — bearing is then a well-defined 0 (Atan2(0, 0)) rather than
    // something the test has to reason about.
    private static readonly Vector3 s_targetPosition = Vector3.zero;

    [Fact]
    public void Keep_A_Claimants_Slot_Stable_Across_Repeated_Claims()
    {
        var slots = new MeleeSlots(slotCount: 6, radius: 1.5f);
        slots.TryClaim(s_target, Creature(1), s_targetPosition, s_targetPosition, out int first);

        // A repeat claim from a DIFFERENT position (and so a different bearing) must not move the
        // claimant to a "closer" slot — the slot is fixed at first claim, not re-picked per tick.
        Assert.True(slots.TryClaim(s_target, Creature(1), s_targetPosition, new Vector3(-5f, 0f, 0f), out int again));
        Assert.Equal(first, again);
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
        Assert.True(slots.TryClaim(s_target, Creature(1), s_targetPosition, new Vector3(1f, 0f, 0f), out int firstSlot));
        Assert.Equal(0, firstSlot);

        // Approaching from directly opposite the target (bearing PI) should claim slot 3, which
        // sits at bearing PI — not slot 1, the lowest still-free index (bearing PI/3).
        Assert.True(slots.TryClaim(s_target, Creature(2), s_targetPosition, new Vector3(-5f, 0f, 0f), out int secondSlot));
        Assert.Equal(3, secondSlot);
    }

    /// <summary>
    /// The slots sit on a horizontal ring of the configured radius. Distance-from-centre alone can't
    /// catch a wrong angle step: |cos, sin| == 1 regardless of the angle, so every slot collapsing onto
    /// a single point still passes that check, and distinct slot indices alone don't guarantee distinct
    /// positions either. So this also pins the actual user-visible requirement — creatures standing in
    /// their slots do not overlap — by requiring every pair of the default 6 slots (at the default 1.5f
    /// radius) to be at least one agent diameter apart: 1.2f, since NavmeshBuildSettings.AgentRadius
    /// defaults to 0.6f.
    /// </summary>
    [Fact]
    public void Place_Slots_On_A_Horizontal_Ring_Of_The_Configured_Radius_At_Least_One_Agent_Diameter_Apart()
    {
        const int SlotCount = 6;
        const float Radius = 1.5f;
        const float AgentDiameter = 1.2f; // 2 * NavmeshBuildSettings.AgentRadius (0.6f)

        var slots = new MeleeSlots(SlotCount, Radius);
        var centre = new Vector3(10f, 5f, 10f);

        var positions = new Vector3[SlotCount];
        for (int slot = 0; slot < SlotCount; slot++)
        {
            positions[slot] = slots.PositionFor(centre, slot);
            Assert.InRange(Vector3.Distance(centre, positions[slot]), Radius - 0.01f, Radius + 0.01f);
            Assert.Equal(centre.y, positions[slot].y); // ring is horizontal
        }

        for (int i = 0; i < SlotCount; i++)
        {
            for (int j = i + 1; j < SlotCount; j++)
            {
                float distance = Vector3.Distance(positions[i], positions[j]);
                Assert.True(distance >= AgentDiameter,
                    $"Slots {i} and {j} are only {distance} apart — closer than one agent diameter ({AgentDiameter}).");
            }
        }
    }

    /// <summary>
    /// No entry is left behind for a target nobody holds a slot on, or every target ever attacked
    /// keeps a permanent one for the life of the instance. <see cref="MeleeSlots.Release"/> must drop
    /// the now-empty per-target dictionary, not just empty it out (2 slots, the last claimant
    /// released). Round 4: the review found the entry-on-success fix landed with no test, silently
    /// revertible with all other tests green: a slotCount of 0 can never produce a successful claim
    /// (the candidate loop never runs), so it must leave no entry behind at all, not one created up
    /// front and left empty. There is no public way to observe this (every public method behaves
    /// identically whether the entry is pruned or merely empty), so this reaches into the private
    /// `_claims` field via reflection rather than asserting through behaviour.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    public void Leave_No_Entry_Behind_For_A_Target_Nobody_Holds_A_Slot_On(int slotCount)
    {
        var slots = new MeleeSlots(slotCount, radius: 1.5f);

        bool claimed = slots.TryClaim(s_target, Creature(1), s_targetPosition, s_targetPosition, out _);
        Assert.Equal(slotCount > 0, claimed);
        if (claimed)
            slots.Release(s_target, Creature(1));

        Assert.False(ClaimsOf(slots).ContainsKey(s_target));
    }

    private static Dictionary<ObjectGuid, Dictionary<ObjectGuid, int>> ClaimsOf(MeleeSlots slots)
    {
        FieldInfo field = typeof(MeleeSlots).GetField("_claims", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Dictionary<ObjectGuid, Dictionary<ObjectGuid, int>>)field.GetValue(slots)!;
    }
}
