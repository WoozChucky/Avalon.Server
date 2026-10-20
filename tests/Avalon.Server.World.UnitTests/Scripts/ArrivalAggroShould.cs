using Avalon.Server.World.UnitTests.Auras;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Scripts.Creatures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Scripts;

public class ArrivalAggroShould
{
    [Fact]
    public void Detect_and_engage_only_after_arrival_protection_ends()
    {
        var clock = new FakeTimeProvider();
        CharacterEntity character = TestCharacters.New(time: clock);
        character.Health = character.CurrentHealth = 100;
        var creature = new Creature { Metadata = Substitute.For<ICreatureMetadata>() };
        ISimulationContext context = Substitute.For<ISimulationContext>();
        context.Characters.Returns(new Dictionary<ObjectGuid, ICharacter> { [character.Guid] = character });
        context.GetNavigatorForPosition(Arg.Any<Vector3>())
            .HasVisibility(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns(true);
        var detector = new CreatureRangeDetectorScript(creature, context, 10);
        var combat = new CreatureCombatScript(NullLoggerFactory.Instance, creature, context, clock);
        detector.CharacterDetected += combat.OnEnteredRange;
        ArrivalTestData.Apply(character);

        detector.Update(TimeSpan.FromSeconds(1));
        combat.OnEnteredRange(character);
        combat.OnAttacked(character);

        Assert.Equal(CreatureRangeDetectorScript.RangeDetectionState.Searching, detector.State);
        Assert.Equal(CreatureCombatScript.CombatState.None, combat.State);

        clock.Advance(TimeSpan.FromSeconds(60));
        detector.Update(TimeSpan.FromSeconds(1));
        Assert.Equal(CreatureRangeDetectorScript.RangeDetectionState.Detected, detector.State);
        Assert.Equal(CreatureCombatScript.CombatState.Combat, combat.State);
    }

    [Fact]
    public void Skip_a_protected_taunter_and_top_threat_then_release_its_last_protected_target()
    {
        var clock = new FakeTimeProvider();
        CharacterEntity arrival = TestCharacters.New(1, time: clock);
        CharacterEntity fighter = TestCharacters.New(2, time: clock);
        arrival.Health = arrival.CurrentHealth = fighter.Health = fighter.CurrentHealth = 100;
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 3),
            Metadata = Substitute.For<ICreatureMetadata>(),
        };
        ISimulationContext context = Substitute.For<ISimulationContext>();
        var encounter = new Encounter(new CombatConfig(), clock);
        encounter.AddHostile(creature);
        encounter.AddPlayer(fighter);
        encounter.AddPlayer(arrival);
        encounter.AddThreat(creature, arrival, 100);
        context.CombatService.GetEncounterFor(creature).Returns(encounter);
        var script = new CreatureCombatScript(NullLoggerFactory.Instance, creature, context, clock);
        script.OnEnteredRange(arrival);
        creature.TauntedBy = arrival;
        creature.TauntExpiresAt = clock.GetUtcNow().UtcDateTime.AddSeconds(10);
        ArrivalTestData.Apply(arrival);

        Assert.Same(fighter, script.PickTarget());
        script.Update(TimeSpan.FromMilliseconds(16));
        Assert.Equal(CreatureCombatScript.CombatState.Combat, script.State);

        ArrivalTestData.Apply(fighter);
        Assert.Null(script.PickTarget());
        script.Update(TimeSpan.FromMilliseconds(16));
        Assert.Equal(CreatureCombatScript.CombatState.Returning, script.State);
    }
}
