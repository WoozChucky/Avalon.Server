using Avalon.Common.Mathematics;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;

namespace Avalon.World.Abilities;

public class AbilityInstance
{
    public required IUnit Caster { get; init; }
    public required IAbility Ability { get; init; }

    /// <summary>Built when the cast was queued, with the aim sent at cast start; fired as it is once the cast time runs out.</summary>
    public required AbilityScript Script { get; init; }
    public required Vector3 CastStartPosition { get; init; }

    /// <summary>The caster's effective haste when the cast started (#627); the cooldown it sets is divided by it.</summary>
    public float HastePct { get; init; }

    /// <summary>The cast's id (#648), unique within the instance: every broadcast of this cast carries it.</summary>
    public uint CastId { get; init; }

    /// <summary>
    /// The footprint the cast's start broadcast carried (#648), the one its script fires on; null when it could not be
    /// resolved. A circle's or a cone's is the dodge wait's when it fires (#1082).
    /// </summary>
    public AbilityFootprint? Footprint { get; init; }

    /// <summary>
    /// Cast by an item (item use: <c>IItemUseHost.CastForItem</c>), never by its caster's own ability: an item use, which a
    /// silence does not stop (#770). A stun ends it like any cast bar.
    /// </summary>
    public bool ForItem { get; init; }

    /// <summary>
    /// Ended by a stun or a silence while the queue was being walked (#770): it is passed over until the walk is done and
    /// removes it. Its own mark, not its ability's <c>Casting</c>, which is not the cast system's alone to clear.
    /// </summary>
    internal bool Ended { get; set; }
}
