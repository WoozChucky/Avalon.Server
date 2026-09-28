using Avalon.Common.ValueObjects;
using Avalon.World.Creatures;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Creatures.Forest;

/// <summary>
/// Blightfly Swarmling (template 6, #163): stings in melee, and spits blight at a player it cannot yet sting.
/// </summary>
/// <remarks>Rotation: Blight Spit while Sting cannot reach (beyond melee, within its 10 m), else Sting. The numbers are the ability rows; the rotation is this code.</remarks>
public sealed class BlightflySwarmlingScript(ILoggerFactory loggerFactory, ICreature creature, ISimulationContext context,
    TimeProvider? time = null, IWorld? world = null)
    : AggroDefendScript(loggerFactory, creature, context,
        new BlightflySwarmlingScript.BlightflySwarmlingCombat(loggerFactory, creature, context, time, world))
{
    /// <summary>Its basic: a 1.5 m cone.</summary>
    public static readonly AbilityId Sting = new(304);

    /// <summary>A projectile with a 10 m reach, 6 s, cast only beyond Sting's reach.</summary>
    public static readonly AbilityId BlightSpit = new(305);

    /// <summary>The abilities this creature fights with, its basic first.</summary>
    public static CreatureAbilityKit Kit { get; } = new(Sting, BlightSpit);

    /// <summary>The creature's rotation. Built by the script above only, so the loader never names it.</summary>
    [ChainedScript]
    internal sealed class BlightflySwarmlingCombat(ILoggerFactory loggerFactory, ICreature creature, ISimulationContext context,
        TimeProvider? time, IWorld? world)
        : CreatureCombatScript(loggerFactory, creature, context, time, world?.Data?.LoadedAbilities, Kit)
    {
        protected override IAbility? ChooseAbility(IUnit target, float distance) =>
            // Blight Spit only while Sting cannot reach: from range, never in melee.
            (Abilities.Basic is { } sting && InReach(sting, distance) ? null : Ready(BlightSpit, distance))
            ?? base.ChooseAbility(target, distance);
    }
}
