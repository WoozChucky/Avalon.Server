using Avalon.Common;
using Avalon.Common.Mathematics;

namespace Avalon.World.Public.Creatures;

/// <summary>
/// Hands out distinct standing positions on a ring around a target, so creatures attacking the
/// same thing surround it instead of stacking on its centre.
/// </summary>
/// <remarks>
/// Declared here because <see cref="Instances.ISimulationContext"/> is a contract surface that
/// lives in Avalon.World.Public, so what it exposes is declared here too — the same reason
/// <see cref="ICreatureLocomotion"/> is declared here rather than alongside its implementation.
/// </remarks>
public interface IMeleeSlots
{
    /// <summary>Idempotent per tick for the same claimant; false once every slot is taken.</summary>
    bool TryClaim(ObjectGuid target, ObjectGuid claimant, out int slot);

    void Release(ObjectGuid target, ObjectGuid claimant);

    /// <summary>Frees every slot on a target — for when the target itself dies or leaves.</summary>
    void ReleaseTarget(ObjectGuid target);

    /// <summary>Frees whatever slot this claimant holds, on whichever target. Safe unconditionally.</summary>
    void ReleaseClaimant(ObjectGuid claimant);

    /// <summary>Where a slot sits, given where the target currently is.</summary>
    Vector3 PositionFor(Vector3 targetPosition, int slot);
}
