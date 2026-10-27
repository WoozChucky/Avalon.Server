using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Scripts.Creatures;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests;

/// <summary>
/// A creature walking home (#610), which every hit ignores: its fight with <c>target</c> begun and given up, then held
/// there, since its walk never runs.
/// </summary>
internal sealed class HomewardScript : CreatureCombatScript
{
    private HomewardScript(ICreature creature, ISimulationContext context)
        : base(NullLoggerFactory.Instance, creature, context)
    {
    }

    public static HomewardScript Walking(ICreature creature, ICharacter target)
    {
        ISimulationContext context = Substitute.For<ISimulationContext>();
        context.MeleeSlots.Returns(Substitute.For<IMeleeSlots>());
        var script = new HomewardScript(creature, context);
        script.OnEnteredRange(target);
        script.OnCharacterLeft(target);
        Assert.Equal(CombatState.Returning, script.State);
        return script;
    }

    public override void Update(TimeSpan deltaTime)
    {
    }
}
