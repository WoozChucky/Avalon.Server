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
/// Old Tuskroot (template 9, Rare, #163): gores; winds up an Earthsplitter, or looses a Thorn Volley from range, when they are ready.
/// </summary>
/// <remarks>Rotation: Earthsplitter, then Thorn Volley, each when ready and in reach; else Tusk Gore. The numbers are the ability rows; the rotation is this code.</remarks>
public sealed class OldTuskrootScript(ILoggerFactory loggerFactory, ICreature creature, ISimulationContext context,
    TimeProvider? time = null, IWorld? world = null)
    : AggroDefendScript(loggerFactory, creature, context,
        new OldTuskrootScript.OldTuskrootCombat(loggerFactory, creature, context, time, world))
{
    /// <summary>Its basic: a 2 m cone.</summary>
    public static readonly AbilityId TuskGore = new(311);

    /// <summary>A 1.2 s wind-up, a 5 m cone, 14 s.</summary>
    public static readonly AbilityId Earthsplitter = new(312);

    /// <summary>A piercing projectile with a 12 m reach, 10 s.</summary>
    public static readonly AbilityId ThornVolley = new(313);

    /// <summary>The abilities this creature fights with, its basic first.</summary>
    public static CreatureAbilityKit Kit { get; } = new(TuskGore, Earthsplitter, ThornVolley);

    /// <summary>The creature's rotation. Built by the script above only, so the loader never names it.</summary>
    [ChainedScript]
    internal sealed class OldTuskrootCombat(ILoggerFactory loggerFactory, ICreature creature, ISimulationContext context,
        TimeProvider? time, IWorld? world)
        : CreatureCombatScript(loggerFactory, creature, context, time, world?.Data?.LoadedAbilities, Kit)
    {
        protected override IAbility? ChooseAbility(IUnit target, float distance) =>
            Ready(Earthsplitter, distance) ?? Ready(ThornVolley, distance) ?? base.ChooseAbility(target, distance);
    }
}
