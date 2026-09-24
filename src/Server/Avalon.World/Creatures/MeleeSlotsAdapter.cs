using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Public.Creatures;

namespace Avalon.World.Creatures;

/// <summary>
/// Adapts the concrete <see cref="MeleeSlots"/> to <see cref="IMeleeSlots"/> so <c>MapInstance</c>
/// can expose it through <c>ISimulationContext</c>.
/// </summary>
/// <remarks>
/// <c>ISimulationContext</c> lives in Avalon.World.Public, which Avalon.World depends on (not the
/// reverse), so it cannot reference <see cref="MeleeSlots"/> directly. This adapter is the seam —
/// <see cref="MeleeSlots"/> itself is untouched.
/// </remarks>
public sealed class MeleeSlotsAdapter(MeleeSlots slots) : IMeleeSlots
{
    public bool TryClaim(ObjectGuid target, ObjectGuid claimant, out int slot) =>
        slots.TryClaim(target, claimant, out slot);

    public void Release(ObjectGuid target, ObjectGuid claimant) => slots.Release(target, claimant);

    public void ReleaseTarget(ObjectGuid target) => slots.ReleaseTarget(target);

    public void ReleaseClaimant(ObjectGuid claimant) => slots.ReleaseClaimant(claimant);

    public Vector3 PositionFor(Vector3 targetPosition, int slot) => slots.PositionFor(targetPosition, slot);
}
