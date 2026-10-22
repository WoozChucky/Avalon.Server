namespace Avalon.World.Abilities;

/// <summary>
/// What fired a projectile (#479), fixed for its flight: a world-state add carries it so a client draws the projectile
/// as its ability and ties its impact to its cast.
/// </summary>
public interface IProjectileIdentity
{
    uint AbilityId { get; }

    /// <summary>The raw guid of the unit that fired it.</summary>
    ulong CasterGuid { get; }

    /// <summary>The cast that fired it, as its start, fired and finish broadcasts carried it.</summary>
    uint CastId { get; }
}
