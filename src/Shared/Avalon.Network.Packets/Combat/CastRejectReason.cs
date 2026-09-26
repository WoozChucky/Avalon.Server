namespace Avalon.Network.Packets.Combat;

/// <summary>
/// Why a CCastAbilityPacket was refused, carried on <see cref="SAbilityNotReadyPacket" /> (#512).
/// Every refusal names one, so a client can tell the player what went wrong.
/// </summary>
public enum CastRejectReason : byte
{
    /// <summary>
    /// No reason was sent. Never sent by this server: it is what a payload without the field
    /// decodes as, so an older payload is never read as a real reason.
    /// </summary>
    Unknown = 0,

    /// <summary>The global cooldown since the last cast has not run out. CooldownMs is what is left of it.</summary>
    Gcd = 1,

    /// <summary>The ability is still cooling down. CooldownMs is what is left of it.</summary>
    Cooldown = 2,

    /// <summary>The ability can only be cast out of combat, and the caster is in combat.</summary>
    RequiresOutOfCombat = 3,

    /// <summary>The ability can only be cast in combat, and the caster is not in combat.</summary>
    RequiresInCombat = 4,

    /// <summary>The caster has less power than the ability costs.</summary>
    NotEnoughPower = 5,

    /// <summary>No longer sent (#164): casts do not name a target. Was: the target is farther away than the ability's range.</summary>
    OutOfRange = 6,

    /// <summary>No longer sent (#164): casts do not name a target. Was: the target named is not in the caster's instance.</summary>
    TargetNotFound = 7,

    /// <summary>No longer sent (#164): casts do not name a target. Was: the target is outside the caster's facing cone.</summary>
    NotFacing = 8,

    /// <summary>The caster is dead.</summary>
    Dead = 9,

    /// <summary>The caster does not have the ability.</summary>
    NotOwned = 10,

    /// <summary>The server could not run the cast. Nothing the player did caused it.</summary>
    InternalError = 11,

    /// <summary>A Cursor skill arrived without a ground point, or with a non-finite component (#164).</summary>
    NoAimPoint = 12,

    /// <summary>Another cast by this caster is still in progress (#521).</summary>
    AlreadyCasting = 13,
}
