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
/// Husk of the Wold (template 7, #163): slams, and bursts with rot around itself when it can.
/// </summary>
/// <remarks>Rotation: Rotting Burst when ready and in reach; else Slam. The numbers are the ability rows; the rotation is this code.</remarks>
public sealed class HuskOfTheWoldScript(ILoggerFactory loggerFactory, ICreature creature, ISimulationContext context,
    TimeProvider? time = null, IWorld? world = null)
    : AggroDefendScript(creature, context,
        new HuskOfTheWoldScript.HuskOfTheWoldCombat(loggerFactory, creature, context, time, world))
{
    /// <summary>Its basic: a 1.8 m cone.</summary>
    public static readonly AbilityId Slam = new(306);

    /// <summary>A 3 m circle on itself, 12 s.</summary>
    public static readonly AbilityId RottingBurst = new(307);

    /// <summary>The abilities this creature fights with, its basic first.</summary>
    public static CreatureAbilityKit Kit { get; } = new(Slam, RottingBurst);

    /// <summary>The creature's rotation. Built by the script above only, so the loader never names it.</summary>
    [ChainedScript]
    internal sealed class HuskOfTheWoldCombat(ILoggerFactory loggerFactory, ICreature creature, ISimulationContext context,
        TimeProvider? time, IWorld? world)
        : CreatureCombatScript(loggerFactory, creature, context, time, world?.Data?.LoadedAbilities, Kit)
    {
        protected override IAbility? ChooseAbility(IUnit target, float distance) =>
            Ready(RottingBurst, distance) ?? base.ChooseAbility(target, distance);
    }
}
