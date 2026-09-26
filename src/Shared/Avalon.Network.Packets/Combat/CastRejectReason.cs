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

    /// <summary>The target is farther away than the ability's range.</summary>
    OutOfRange = 6,

    /// <summary>The target named is not a creature or character in the caster's instance.</summary>
    TargetNotFound = 7,

    /// <summary>
    /// The target is outside the caster's facing cone: its angle from the caster's facing is not
    /// strictly less than AbilityInfo.FacingAngle.
    /// </summary>
    NotFacing = 8,

    /// <summary>The caster is dead.</summary>
    Dead = 9,

    /// <summary>The caster does not have the ability.</summary>
    NotOwned = 10,

    /// <summary>The server could not run the cast. Nothing the player did caused it.</summary>
    InternalError = 11,
}
