using Avalon.Common;
using Avalon.Common.Mathematics;

namespace Avalon.World.Public.Creatures;

/// <summary>
/// Hands out distinct standing positions on a ring around a target, so creatures attacking the
/// same thing surround it instead of stacking on its centre.
/// </summary>
/// <remarks>
/// Declared here, rather than exposing <c>Avalon.World.Creatures.MeleeSlots</c> directly on
/// <see cref="Instances.ISimulationContext"/>: <c>ISimulationContext</c> lives in
/// Avalon.World.Public, which Avalon.World depends on (not the reverse), so the concrete
/// implementation type in Avalon.World is not visible from this project. <c>MapInstance</c>
/// exposes its concrete <c>MeleeSlots</c> through this interface via an adapter, so the concrete
/// type itself never has to move or change.
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
