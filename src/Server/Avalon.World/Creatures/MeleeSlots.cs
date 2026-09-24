using Avalon.Common;
using Avalon.Common.Mathematics;

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
public sealed class MeleeSlots(int slotCount, float radius)
{
    private readonly Dictionary<ObjectGuid, Dictionary<ObjectGuid, int>> _claims = [];

    public bool TryClaim(ObjectGuid target, ObjectGuid claimant, out int slot)
    {
        if (!_claims.TryGetValue(target, out Dictionary<ObjectGuid, int>? taken))
        {
            taken = [];
            _claims[target] = taken;
        }

        // Re-claiming is idempotent: a chaser calls this every tick it is chasing.
        if (taken.TryGetValue(claimant, out slot))
            return true;

        for (int candidate = 0; candidate < slotCount; candidate++)
        {
            if (taken.ContainsValue(candidate))
                continue;

            taken[claimant] = candidate;
            slot = candidate;
            return true;
        }

        slot = -1;
        return false;
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
        float angle = slot * 2f * MathF.PI / slotCount;
        return new Vector3(
            targetPosition.x + MathF.Cos(angle) * radius,
            targetPosition.y,
            targetPosition.z + MathF.Sin(angle) * radius);
    }
}
