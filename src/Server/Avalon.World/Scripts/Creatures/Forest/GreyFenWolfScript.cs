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
/// Grey Fen Wolf (template 5, #163): bites, and rakes with a Ravenous Claw when it can.
/// </summary>
/// <remarks>Rotation: Ravenous Claw when ready and in reach; else Bite. The numbers are the ability rows; the rotation is this code.</remarks>
public sealed class GreyFenWolfScript(ILoggerFactory loggerFactory, ICreature creature, ISimulationContext context,
    TimeProvider? time = null, IWorld? world = null)
    : AggroDefendScript(creature, context,
        new GreyFenWolfScript.GreyFenWolfCombat(loggerFactory, creature, context, time, world))
{
    /// <summary>Its basic: a 1.8 m cone.</summary>
    public static readonly AbilityId Bite = new(302);

    /// <summary>A 2.5 m cone, 8 s.</summary>
    public static readonly AbilityId RavenousClaw = new(303);

    /// <summary>The abilities this creature fights with, its basic first.</summary>
    public static CreatureAbilityKit Kit { get; } = new(Bite, RavenousClaw);

    /// <summary>The creature's rotation. Built by the script above only, so the loader never names it.</summary>
    [ChainedScript]
    internal sealed class GreyFenWolfCombat(ILoggerFactory loggerFactory, ICreature creature, ISimulationContext context,
        TimeProvider? time, IWorld? world)
        : CreatureCombatScript(loggerFactory, creature, context, time, world?.Data?.LoadedAbilities, Kit)
    {
        protected override IAbility? ChooseAbility(IUnit target, float distance) =>
            Ready(RavenousClaw, distance) ?? base.ChooseAbility(target, distance);
    }
}
