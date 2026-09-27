using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Combat;
using Avalon.World.Creatures;
using Avalon.World.Entities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Avalon.World.Scripts.Creatures;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Combat;

/// <summary>
/// #506 review: a dodged opening hit deals nothing, so the creature's script is not hit, but it still
/// engages the creature against its attacker, as a landed hit would; through every script that chains
/// the combat script too. The attacker stands 20 m off, beyond any detection range, as a Hunter opening
/// from range would.
/// </summary>
public class DodgeEngagesShould
{
    public static TheoryData<string> Scripts() =>
        new() { nameof(CreatureCombatScript), nameof(AggroDefendScript), nameof(CreaturePatrolScript) };

    [Theory]
    [MemberData(nameof(Scripts))]
    public void Engage_the_creature_against_the_attacker_on_a_dodged_opening_hit(string scriptName)
    {
        var locomotion = Substitute.For<ICreatureLocomotion>();
        Vector3? requested = null;
        var ctx = Substitute.For<ISimulationContext>();
        ctx.Characters.Returns(new Dictionary<ObjectGuid, ICharacter>());
        ctx.Locomotion.Returns(locomotion);
        ctx.MeleeSlots.Returns(new MeleeSlots(6, radius: 1.5f));
        var observer = Substitute.For<ICombatService>();
        observer.GetEncounterFor(Arg.Any<IUnit>()).Returns((IEncounter?)null);
        ctx.CombatService.Returns(observer);

        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 506_701),
            Metadata = Substitute.For<ICreatureMetadata>(),
            Name = "Wolf",
            Health = 100,
            CurrentHealth = 100,
            DodgePct = 30f,
            Level = 1,
        };
        locomotion.When(l => l.MoveTo(creature, Arg.Any<Vector3>())).Do(ci => requested = ci.ArgAt<Vector3>(1));
        locomotion.ResolvedDestination(creature).Returns(_ => requested);
        locomotion.HasArrived(creature).Returns(true);

        AiScript script = scriptName switch
        {
            nameof(CreatureCombatScript) => new CreatureCombatScript(NullLoggerFactory.Instance, creature, ctx),
            nameof(AggroDefendScript) => new AggroDefendScript(NullLoggerFactory.Instance, creature, ctx),
            _ => new CreaturePatrolScript(NullLoggerFactory.Instance, creature, ctx),
        };
        creature.Script = script;

        CharacterEntity hunter = Inventory.TestCharacters.New(506_702);
        hunter.Position = new Vector3(20f, 0f, 0f);
        var svc = new CombatService(new CombatConfig(), new EncounterRegistry(new CombatConfig()), ctx,
            random: new ScriptedCombatRandom(0.0));

        svc.ApplyDamage(hunter, creature, 10);
        script.Update(TimeSpan.FromSeconds(0.1));

        Assert.Equal(100u, creature.CurrentHealth);
        Assert.NotNull(requested);
        Assert.True(Vector3.Distance(requested!.Value, hunter.Position) <= 2f,
            $"the creature set off for {requested}, not toward its attacker at {hunter.Position}");
        if (script is CreatureCombatScript combat)
            Assert.Equal(CreatureCombatScript.CombatState.Combat, combat.State);
    }
}
