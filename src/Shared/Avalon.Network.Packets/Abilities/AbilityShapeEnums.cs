namespace Avalon.Network.Packets.Abilities;

/// <summary>How a skill aims (#164). Stored on AbilityTemplate and sent on AbilityInfo. Append-only.</summary>
public enum AbilityAimMode : byte
{
    /// <summary>Along the caster's facing, its yaw from movement input.</summary>
    Movement = 0,

    /// <summary>At a ground point: CCastAbilityPacket.GroundPos.</summary>
    Cursor = 1,
}

/// <summary>The area a skill hits: a circle, a cone, or a projectile's path. Append-only.</summary>
public enum AbilityShape : byte
{
    Circle = 0,
    Cone = 1,
    Projectile = 2,
}

/// <summary>Where a circle is centred. Meaningless for the other shapes. Append-only.</summary>
public enum AbilityAnchor : byte
{
    Caster = 0,
    AimPoint = 1,
}

/// <summary>Whom a skill affects: hostile units are damaged, allies are healed. Append-only.</summary>
public enum AbilityAffects : byte
{
    Hostile = 0,
    Ally = 1,
}
