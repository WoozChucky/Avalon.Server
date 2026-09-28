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
/// Bramblemaw Alpha (template 8, Elite, #163): mauls; winds up a Howling Roar, then a Rending Frenzy, when they are ready.
/// </summary>
/// <remarks>Rotation: Howling Roar, then Rending Frenzy, each when ready and in reach; else Maul. The numbers are the ability rows; the rotation is this code.</remarks>
public sealed class BramblemawAlphaScript(ILoggerFactory loggerFactory, ICreature creature, ISimulationContext context,
    TimeProvider? time = null, IWorld? world = null)
    : AggroDefendScript(loggerFactory, creature, context,
        new BramblemawAlphaScript.BramblemawAlphaCombat(loggerFactory, creature, context, time, world))
{
    /// <summary>Its basic: a 2 m cone.</summary>
    public static readonly AbilityId Maul = new(308);

    /// <summary>A 2.5 m cone, 9 s.</summary>
    public static readonly AbilityId RendingFrenzy = new(309);

    /// <summary>A 1 s wind-up, a 5 m circle on itself, 15 s.</summary>
    public static readonly AbilityId HowlingRoar = new(310);

    /// <summary>The abilities this creature fights with, its basic first.</summary>
    public static CreatureAbilityKit Kit { get; } = new(Maul, HowlingRoar, RendingFrenzy);

    /// <summary>The creature's rotation. Built by the script above only, so the loader never names it.</summary>
    [ChainedScript]
    internal sealed class BramblemawAlphaCombat(ILoggerFactory loggerFactory, ICreature creature, ISimulationContext context,
        TimeProvider? time, IWorld? world)
        : CreatureCombatScript(loggerFactory, creature, context, time, world?.Data?.LoadedAbilities, Kit)
    {
        protected override IAbility? ChooseAbility(IUnit target, float distance) =>
            Ready(HowlingRoar, distance) ?? Ready(RendingFrenzy, distance) ?? base.ChooseAbility(target, distance);
    }
}
