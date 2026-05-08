using Avalon.World.Combat;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Units;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Combat;

public class EncounterRegistryShould
{
    [Fact]
    public void Should_create_and_track_encounter()
    {
        var reg = new EncounterRegistry(new CombatConfig());
        var enc = reg.CreateEncounter();
        Assert.Contains(enc, reg.Active);
    }

    [Fact]
    public void Should_find_encounter_containing_a_unit()
    {
        var reg = new EncounterRegistry(new CombatConfig());
        var enc = (Encounter)reg.CreateEncounter();
        var u   = Substitute.For<IUnit>();
        enc.AddHostile(u);
        Assert.Same(enc, reg.FindEncounterContaining(u));
    }

    [Fact]
    public void Should_return_null_when_no_encounter_contains_unit()
    {
        var reg = new EncounterRegistry(new CombatConfig());
        var u   = Substitute.For<IUnit>();
        Assert.Null(reg.FindEncounterContaining(u));
    }

    [Fact]
    public void Should_remove_encounter_on_dispose()
    {
        var reg = new EncounterRegistry(new CombatConfig());
        var enc = reg.CreateEncounter();
        reg.Dispose(enc);
        Assert.DoesNotContain(enc, reg.Active);
    }
}
