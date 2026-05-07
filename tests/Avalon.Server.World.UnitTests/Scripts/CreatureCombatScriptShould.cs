using System;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Units;
using Avalon.World.Scripts.Creatures;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Scripts;

public class CreatureCombatScriptShould
{
    [Fact]
    public void Should_pick_top_threat_attacker_as_target()
    {
        var (script, encounter, _) = BuildScript(out var creature);

        ICharacter attacker = Substitute.For<ICharacter>();
        encounter.GetTopThreat(creature).Returns(attacker);

        IUnit? picked = script.PickTarget();

        Assert.Same(attacker, picked);
    }

    [Fact]
    public void Should_return_null_when_no_encounter_exists_and_no_taunt()
    {
        var (script, _, combat) = BuildScript(out var creature);

        // No encounter for this creature.
        combat.GetEncounterFor(creature).Returns((IEncounter?)null);

        IUnit? picked = script.PickTarget();

        Assert.Null(picked);
    }

    [Fact]
    public void Should_honor_taunt_until_expiry()
    {
        var (script, encounter, _) = BuildScript(out var creature);

        ICharacter tank = Substitute.For<ICharacter>();
        ICharacter dps  = Substitute.For<ICharacter>();

        // DPS would normally be top-threat, but tank has an active taunt.
        encounter.GetTopThreat(creature).Returns(dps);
        creature.TauntedBy      = tank;
        creature.TauntExpiresAt = DateTime.UtcNow.AddSeconds(5);

        IUnit? picked = script.PickTarget();

        Assert.Same(tank, picked);
    }

    [Fact]
    public void Should_revert_to_top_threat_after_taunt_expires()
    {
        var (script, encounter, _) = BuildScript(out var creature);

        ICharacter tank = Substitute.For<ICharacter>();
        ICharacter dps  = Substitute.For<ICharacter>();

        encounter.GetTopThreat(creature).Returns(dps);

        // Taunt that has already expired.
        creature.TauntedBy      = tank;
        creature.TauntExpiresAt = DateTime.UtcNow.AddSeconds(-1);

        IUnit? picked = script.PickTarget();

        Assert.Same(dps, picked);
    }

    [Fact]
    public void Should_prefer_taunter_over_top_threat_even_when_threat_higher()
    {
        // Taunt is an authoritative override, not a tiebreaker. Even if GetTopThreat returns a
        // unit that isn't the taunter (e.g. the threat list disagrees with the taunt due to
        // floating-point edge cases), the taunter wins while the taunt is active.
        var (script, encounter, _) = BuildScript(out var creature);

        ICharacter tank      = Substitute.For<ICharacter>();
        ICharacter someoneElse = Substitute.For<ICharacter>();

        encounter.GetTopThreat(creature).Returns(someoneElse);
        creature.TauntedBy      = tank;
        creature.TauntExpiresAt = DateTime.UtcNow.AddSeconds(2);

        IUnit? picked = script.PickTarget();

        Assert.Same(tank, picked);
    }

    private static (CreatureCombatScript script, IEncounter encounter, ICombatService combat) BuildScript(out ICreature creature)
    {
        creature = Substitute.For<ICreature>();
        // NSubstitute auto-substitutes reference-type reads. Initialise the taunt fields so
        // the no-taunt branch in PickTarget is the default rather than picking up an
        // auto-stubbed IUnit.
        creature.TauntedBy      = null;
        creature.TauntExpiresAt = DateTime.MinValue;
        creature.Metadata.Returns(Substitute.For<ICreatureMetadata>());

        var encounter = Substitute.For<IEncounter>();
        var combat    = Substitute.For<ICombatService>();
        combat.GetEncounterFor(creature).Returns(encounter);

        var context = Substitute.For<ISimulationContext>();
        context.CombatService.Returns(combat);

        var script = new CreatureCombatScript(NullLoggerFactory.Instance, creature, context);
        return (script, encounter, combat);
    }
}
