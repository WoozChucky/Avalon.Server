using Avalon.World.Public.Creatures;

namespace Avalon.World.Creatures.Locomotion;

/// <summary>
/// A locomotion whose <see cref="ICreatureLocomotion.SyncPlayer" /> records something, so
/// <c>MapInstance</c> tells it where every player is each tick when
/// <c>GameConfiguration.CrowdIncludesPlayers</c> is on. <see cref="CrowdLocomotion" /> is the one in
/// production; a decorator around it (the crowd budget benchmark's, #638) implements it too, so the
/// instance's own sync reaches the crowd through it. World-side, not on the modding API.
/// </summary>
public interface IPlayerAwareLocomotion : ICreatureLocomotion
{
}
