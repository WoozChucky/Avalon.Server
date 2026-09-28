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
/// Thornback Boar (template 4, #163): gores, and tramples everyone close when it can.
/// </summary>
/// <remarks>Rotation: Trample when ready and in reach; else Gore. The numbers are the ability rows; the rotation is this code.</remarks>
public sealed class ThornbackBoarScript(ILoggerFactory loggerFactory, ICreature creature, ISimulationContext context,
    TimeProvider? time = null, IWorld? world = null)
    : AggroDefendScript(loggerFactory, creature, context,
        new ThornbackBoarScript.ThornbackBoarCombat(loggerFactory, creature, context, time, world))
{
    /// <summary>Its basic: a 1.8 m cone.</summary>
    public static readonly AbilityId Gore = new(300);

    /// <summary>A 2.5 m circle on itself, 10 s.</summary>
    public static readonly AbilityId Trample = new(301);

    /// <summary>The abilities this creature fights with, its basic first.</summary>
    public static CreatureAbilityKit Kit { get; } = new(Gore, Trample);

    /// <summary>The creature's rotation. Built by the script above only, so the loader never names it.</summary>
    [ChainedScript]
    internal sealed class ThornbackBoarCombat(ILoggerFactory loggerFactory, ICreature creature, ISimulationContext context,
        TimeProvider? time, IWorld? world)
        : CreatureCombatScript(loggerFactory, creature, context, time, world?.Data?.LoadedAbilities, Kit)
    {
        protected override IAbility? ChooseAbility(IUnit target, float distance) =>
            Ready(Trample, distance) ?? base.ChooseAbility(target, distance);
    }
}
