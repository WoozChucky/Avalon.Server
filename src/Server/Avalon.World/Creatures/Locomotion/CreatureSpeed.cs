using Avalon.World.Entities;
using Avalon.World.Public.Creatures;

namespace Avalon.World.Creatures.Locomotion;

/// <summary>
/// How fast a creature walks this tick: the Speed its script set, times the World-side creature's aura speed factor (a
/// slow). Any other creature walks at its Speed. Read by both locomotions, so they agree.
/// </summary>
public static class CreatureSpeed
{
    public static float Of(ICreature creature) => creature is Creature world ? creature.Speed * world.SpeedFactor : creature.Speed;
}
