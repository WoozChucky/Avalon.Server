using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Public.Creatures;

namespace Avalon.World.Creatures;

/// <summary>
/// Hands out distinct standing positions on a ring around a target, so creatures attacking the same
/// thing surround it instead of stacking on its centre.
/// </summary>
/// <remarks>
/// Reservation is keyed by target rather than held per creature: two creatures choosing an angle
/// independently would pick the same one. Angles are world-fixed rather than relative to the
/// target's facing, because facing-relative slots move every time the player turns and would
/// re-path every chaser for no visual gain.
/// </remarks>
public sealed class MeleeSlots(int slotCount, float radius) : IMeleeSlots
{
    private readonly Dictionary<ObjectGuid, Dictionary<ObjectGuid, int>> _claims = [];

    /// <summary>
    /// Claims the free slot nearest <paramref name="claimantPosition"/>'s bearing from
    /// <paramref name="targetPosition"/> — not simply the lowest free index, which is unrelated
    /// to where the claimant is actually standing and would send it on a long tangential walk
    /// around the ring to reach a far slot when a near one was free.
    /// </summary>
    public bool TryClaim(ObjectGuid target, ObjectGuid claimant, Vector3 targetPosition, Vector3 claimantPosition, out int slot)
    {
        _claims.TryGetValue(target, out Dictionary<ObjectGuid, int>? taken);

        // Re-claiming is idempotent: a chaser calls this every tick it is chasing. The slot
        // already picked stays fixed even if the claimant's position (and so its bearing) has
        // moved since — re-evaluating bearing every tick would let a creature's slot drift as it
        // walks, which is exactly the kind of movement decision this type exists to avoid.
        if (taken is not null && taken.TryGetValue(claimant, out slot))
            return true;

        float bearing = MathF.Atan2(claimantPosition.z - targetPosition.z, claimantPosition.x - targetPosition.x);

        int best = -1;
        float bestDelta = float.MaxValue;
        for (int candidate = 0; candidate < slotCount; candidate++)
        {
            if (taken is not null && taken.ContainsValue(candidate))
                continue;

            float delta = AngleDelta(bearing, AngleFor(candidate));
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = candidate;
            }
        }

        if (best == -1)
        {
            slot = -1;
            return false;
        }

        // Only create the per-target entry once a claim actually succeeds — inserting it
        // unconditionally up front (e.g. before this loop ever ran) would leave an empty entry
        // behind for a target nobody ever successfully claimed a slot on (slotCount == 0, most
        // simply), the exact leak Release's pruning below exists to prevent on the way out.
        if (taken is null)
        {
            taken = [];
            _claims[target] = taken;
        }

        taken[claimant] = best;
        slot = best;
        return true;
    }

    public void Release(ObjectGuid target, ObjectGuid claimant)
    {
        if (!_claims.TryGetValue(target, out Dictionary<ObjectGuid, int>? taken))
            return;

        taken.Remove(claimant);

        // Prune the now-empty inner dictionary — otherwise every target ever attacked leaves a
        // permanent (if empty) entry behind for the life of the instance.
        if (taken.Count == 0)
            _claims.Remove(target);
    }

    /// <summary>Frees every slot on a target — for when the target itself dies or leaves.</summary>
    public void ReleaseTarget(ObjectGuid target) => _claims.Remove(target);

    /// <summary>
    /// Frees whatever slot this claimant holds, on whichever target, without the caller needing to
    /// know which target that was. Removal paths that don't run through the script that made the
    /// claim — a creature despawned mid-combat, say — have no other way to give the slot back, and
    /// an unreleased slot is lost for the life of the instance.
    /// </summary>
    public void ReleaseClaimant(ObjectGuid claimant)
    {
        foreach (ObjectGuid target in _claims
                     .Where(entry => entry.Value.ContainsKey(claimant))
                     .Select(entry => entry.Key)
                     .ToList())
        {
            Release(target, claimant);
        }
    }

    /// <summary>Where a slot sits, given where the target currently is.</summary>
    public Vector3 PositionFor(Vector3 targetPosition, int slot)
    {
        float angle = AngleFor(slot);
        return new Vector3(
            targetPosition.x + MathF.Cos(angle) * radius,
            targetPosition.y,
            targetPosition.z + MathF.Sin(angle) * radius);
    }

    private float AngleFor(int slot) => slot * 2f * MathF.PI / slotCount;

    /// <summary>Absolute circular difference between two angles in radians, in [0, PI].</summary>
    private static float AngleDelta(float a, float b)
    {
        float diff = (a - b) % (2f * MathF.PI);
        if (diff < 0f)
            diff += 2f * MathF.PI;

        return MathF.Min(diff, 2f * MathF.PI - diff);
    }
}
