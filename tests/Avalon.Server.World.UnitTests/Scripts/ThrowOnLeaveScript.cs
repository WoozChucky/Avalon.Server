using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;

namespace Avalon.Server.World.UnitTests.Scripts;

/// <summary>A creature script whose departure hook throws (#546), as a broken one could.</summary>
public sealed class ThrowOnLeaveScript(ICreature creature, ISimulationContext context) : AiScript(creature, context)
{
    public override object State { get; set; } = 0;

    protected override bool ShouldRun() => false;

    public override void OnCharacterLeft(ICharacter character) =>
        throw new InvalidOperationException("simulated departure hook failure");
}
