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
/// Mother Bramble (template 10, Boss, #163): lashes; winds up a Bramble Nova, or sprays thorns, when they are ready.
/// </summary>
/// <remarks>Rotation: Bramble Nova, then Thornspray, each when ready and in reach; else Bramble Lash. The numbers are the ability rows; the rotation is this code.</remarks>
public sealed class MotherBrambleScript(ILoggerFactory loggerFactory, ICreature creature, ISimulationContext context,
    TimeProvider? time = null, IWorld? world = null)
    : AggroDefendScript(creature, context,
        new MotherBrambleScript.MotherBrambleCombat(loggerFactory, creature, context, time, world))
{
    /// <summary>Its basic: a 2.5 m cone.</summary>
    public static readonly AbilityId BrambleLash = new(314);

    /// <summary>A 1.2 s wind-up, a 6 m circle on itself, 16 s.</summary>
    public static readonly AbilityId BrambleNova = new(315);

    /// <summary>A 5 m cone, 8 s.</summary>
    public static readonly AbilityId Thornspray = new(316);

    /// <summary>The abilities this creature fights with, its basic first.</summary>
    public static CreatureAbilityKit Kit { get; } = new(BrambleLash, BrambleNova, Thornspray);

    /// <summary>The creature's rotation. Built by the script above only, so the loader never names it.</summary>
    [ChainedScript]
    internal sealed class MotherBrambleCombat(ILoggerFactory loggerFactory, ICreature creature, ISimulationContext context,
        TimeProvider? time, IWorld? world)
        : CreatureCombatScript(loggerFactory, creature, context, time, world?.Data?.LoadedAbilities, Kit)
    {
        protected override IAbility? ChooseAbility(IUnit target, float distance) =>
            Ready(BrambleNova, distance) ?? Ready(Thornspray, distance) ?? base.ChooseAbility(target, distance);
    }
}
