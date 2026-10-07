using System.Runtime.CompilerServices;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.World.Abilities;
using Avalon.World.Creatures;
using Avalon.World.Entities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Avalon.World.Scripts.Creatures;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Scripts;

/// <summary>
/// The one test ability kit (#163): a basic that plays the part the raw swing used to, a 1.8 m cone (the
/// seeded basics' reach) with a 2.25 s cooldown, the seeded swing interval.
/// </summary>
internal static class TestKit
{
    public static readonly AbilityId BasicId = new(90_163);

    public static AbilityTemplate Basic()
    {
        AbilityTemplate row = AbilityTestData.Cone(BasicId.Value, reach: 1.8f, arc: 90f);
        row.Name = "Test basic";
        row.EffectValue = 0;
        row.BaseDamageCoefficient = 1.0f;
        row.Cooldown = 2250;
        row.AllowedClasses = [];
        return row;
    }

    public static AbilityCatalog Catalog(params AbilityTemplate[] extra) =>
        new([Basic(), .. extra], NullLoggerFactory.Instance);

    public static CreatureAbilityKit Kit => new(BasicId);
}

/// <summary>
/// A combat script carrying <see cref="TestKit" />, for tests that drive the script over a substitute context.
/// It registers itself with <see cref="CastRig" />, which plays the cast system for that context. Chained, so
/// the script loader never names it.
/// </summary>
[ChainedScript]
internal sealed class KitCombatScript : CreatureCombatScript
{
    public KitCombatScript(ICreature creature, ISimulationContext context, TimeProvider? time = null,
        AbilityCatalog? catalog = null, CreatureAbilityKit? kit = null)
        : base(NullLoggerFactory.Instance, creature, context, time, catalog ?? TestKit.Catalog(), kit ?? TestKit.Kit)
    {
        // A real instance casts for itself; only a substitute context needs the rig.
        if (context is not Avalon.World.Instances.MapInstance)
            CastRig.Register(context, creature, this);
    }

    public IUnit? CurrentTarget => Target;

    public new CreatureAbilities Abilities => base.Abilities;

    /// <summary>When set, answers <see cref="ChooseAbility" /> in place of the base rotation.</summary>
    public Func<IUnit, float, IAbility?>? Rotation { get; set; }

    protected override IAbility? ChooseAbility(IUnit target, float distance) =>
        Rotation is { } rotation ? rotation(target, distance) : base.ChooseAbility(target, distance);

    public IAbility? ReadyFor(AbilityId id, float distance) => Ready(id, distance);
}

/// <summary>Aggro detection chained to a <see cref="KitCombatScript" />.</summary>
[ChainedScript]
internal sealed class KitAggroDefendScript(ICreature creature, ISimulationContext context)
    : AggroDefendScript(NullLoggerFactory.Instance, creature, context, new KitCombatScript(creature, context));

/// <summary>A patrol whose fights use a <see cref="KitCombatScript" />.</summary>
[ChainedScript]
internal sealed class KitPatrolScript(ICreature creature, ISimulationContext context)
    : CreaturePatrolScript(creature, context, new KitCombatScript(creature, context));

/// <summary>
/// Plays the cast system for a substitute <see cref="ISimulationContext" /> (#163): an instant cast sets the
/// cooldown the real one would (a World-side creature's basic its SwingInterval, anything else its row's) and
/// hits the caster's script's current target through the context's combat service with the ability, as a
/// shape script would. Every cast, instant or queued, is recorded. A queued cast is taken and marked casting,
/// and hits nothing until <see cref="Complete" /> fires it.
/// </summary>
internal sealed class CastRig
{
    private static readonly ConditionalWeakTable<ISimulationContext, CastRig> s_rigs = new();

    private readonly ISimulationContext _context;
    private readonly Dictionary<IUnit, KitCombatScript> _scripts = new(ReferenceEqualityComparer.Instance);

    private CastRig(ISimulationContext context)
    {
        _context = context;
        context.RunInstantAbility(Arg.Any<IUnit>(), Arg.Any<AbilityAim>(), Arg.Any<IAbility>())
            .Returns(ci => Instant(ci.ArgAt<IUnit>(0), ci.ArgAt<AbilityAim>(1), ci.ArgAt<IAbility>(2)));
        context.QueueAbility(Arg.Any<IUnit>(), Arg.Any<AbilityAim>(), Arg.Any<IAbility>())
            .Returns(ci => Queue(ci.ArgAt<IUnit>(0), ci.ArgAt<AbilityAim>(1), ci.ArgAt<IAbility>(2)));
    }

    /// <summary>Every cast started, instant or queued, in order.</summary>
    public List<(IUnit Caster, AbilityAim Aim, IAbility Ability)> Casts { get; } = [];

    public static CastRig Of(ISimulationContext context) => s_rigs.GetValue(context, c => new CastRig(c));

    public static void Register(ISimulationContext context, IUnit creature, KitCombatScript script) =>
        Of(context)._scripts[creature] = script;

    /// <summary>Fires a queued cast now, as the cast system does when its time runs out.</summary>
    public void Complete(IUnit caster, IAbility ability)
    {
        ability.Casting = false;
        ability.CastTimeTimer = ability.Metadata.CastTime;
        Fire(caster, ability);
    }

    private bool Instant(IUnit caster, AbilityAim aim, IAbility ability)
    {
        Casts.Add((caster, aim, ability));
        Fire(caster, ability);
        return true;
    }

    private bool Queue(IUnit caster, AbilityAim aim, IAbility ability)
    {
        Casts.Add((caster, aim, ability));
        ability.Casting = true;
        return true;
    }

    private void Fire(IUnit caster, IAbility ability)
    {
        ability.CooldownTimer = caster is Creature creature && creature.Abilities.IsBasic(ability)
            ? creature.SwingInterval
            : ability.Metadata.Cooldown;

        if (_scripts.TryGetValue(caster, out KitCombatScript? script) && script.CurrentTarget is { } target)
            _context.CombatService.ApplyDamage(caster, target, ability.Metadata.EffectValue, ability);
    }
}
