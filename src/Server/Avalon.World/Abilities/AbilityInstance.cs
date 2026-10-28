using Avalon.Common.Mathematics;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;

namespace Avalon.World.Abilities;

/// <summary>
/// A queued cast, waiting out its cast time. Each cast system reuses its entries (#1181 Part D): one is started for a
/// cast it queues (<see cref="Start" />) and cleared once the cast has left the queue (<see cref="Clear" />), so a
/// queued cast of any shape allocates no entry once warm.
/// </summary>
public class AbilityInstance
{
    public IUnit Caster { get; private set; } = null!;
    public IAbility Ability { get; private set; } = null!;

    /// <summary>Built when the cast was queued, with the aim sent at cast start; fired as it is once the cast time runs out.</summary>
    public AbilityScript Script { get; private set; } = null!;
    public Vector3 CastStartPosition { get; private set; }

    /// <summary>
    /// The cooldown the cast sets when it fires, in seconds, worked out from its caster's pace when it was queued (#1178), so
    /// a gear change mid-cast (a weapon swap) changes nothing.
    /// </summary>
    public float CooldownSeconds { get; private set; }

    /// <summary>The cast's id (#648), unique within the instance: every broadcast of this cast carries it.</summary>
    public uint CastId { get; private set; }

    /// <summary>
    /// The footprint the cast's start broadcast carried (#648), the one its script fires on; null when it could not be
    /// resolved. A circle's or a cone's is the dodge wait's when it fires (#1082).
    /// </summary>
    public AbilityFootprint? Footprint { get; private set; }

    /// <summary>
    /// Cast by an item (item use: <c>IItemUseHost.CastForItem</c>), never by its caster's own ability: an item use, which a
    /// silence does not stop (#770). A stun ends it like any cast bar.
    /// </summary>
    public bool ForItem { get; private set; }

    /// <summary>
    /// Ended by a stun or a silence while the queue was being walked (#770): it is passed over until the walk is done and
    /// removes it. Its own mark, not its ability's <c>Casting</c>, which is not the cast system's alone to clear.
    /// </summary>
    internal bool Ended { get; set; }

    /// <summary>Its script was fired: the running list, not this entry, decides where the script goes when it ends.</summary>
    internal bool Fired { get; set; }

    /// <summary>Back on its cast system's free stack, until it is started again: a second return is a fault.</summary>
    internal bool Returned { get; set; }

    /// <summary>The entry of a cast just queued: every member as given, neither ended nor fired.</summary>
    internal void Start(IUnit caster, IAbility ability, AbilityScript script, Vector3 castStartPosition, float cooldownSeconds,
        uint castId, AbilityFootprint? footprint, bool forItem)
    {
        Caster = caster;
        Ability = ability;
        Script = script;
        CastStartPosition = castStartPosition;
        CooldownSeconds = cooldownSeconds;
        CastId = castId;
        Footprint = footprint;
        ForItem = forItem;
        Ended = false;
        Fired = false;
        Returned = false;
    }

    /// <summary>The entry put away once its cast left the queue: no caster, ability or script kept alive by it.</summary>
    internal void Clear()
    {
        Caster = null!;
        Ability = null!;
        Script = null!;
        Footprint = null;
    }
}
