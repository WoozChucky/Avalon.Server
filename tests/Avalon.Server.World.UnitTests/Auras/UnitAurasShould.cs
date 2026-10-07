using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Auras;
using Avalon.World.Entities;

namespace Avalon.Server.World.UnitTests.Auras;

/// <summary>A unit's auras: keys, lookups, the client changes they owe, and the stats they add.</summary>
public class UnitAurasShould
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly ObjectGuid Hunter = new(ObjectType.Character, 7);
    private static readonly ObjectGuid Wizard = new(ObjectType.Character, 8);

    private static ActiveAura Aura(AuraTemplate template, ObjectGuid caster) =>
        new(template, caster, AuraSource.None, 1, new AuraSnapshot(5f, 0f, 1),
            AuraSchedule.Start(T0, template.DurationMs, template.TickIntervalMs), template.DurationMs, T0.UtcDateTime);

    [Fact]
    public void Key_each_aura_and_record_its_application()
    {
        var auras = new UnitAuras();
        ActiveAura bleed = Aura(AuraTestData.Bleed(), Hunter);
        ActiveAura burn = Aura(AuraTestData.Burn(), Wizard);

        auras.Add(bleed, T0);
        auras.Add(burn, T0.AddSeconds(1));

        Assert.Equal((1u, 2u), (bleed.Key, burn.Key));
        Assert.Equal(2, auras.Count);
        Assert.Equal(
            [new AuraChange(new AuraId(901), 1, Hunter.RawValue, 1, 12000, 12000, AuraChangeKind.Applied),
             new AuraChange(new AuraId(902), 2, Wizard.RawValue, 1, 8000, 9000, AuraChangeKind.Applied)],
            auras.Changes);
    }

    [Fact]
    public void Find_an_aura_by_id_or_by_id_and_caster()
    {
        var auras = new UnitAuras();
        ActiveAura fromHunter = Aura(AuraTestData.Independent(), Hunter);
        ActiveAura fromWizard = Aura(AuraTestData.Independent(), Wizard);
        auras.Add(fromHunter, T0);
        auras.Add(fromWizard, T0);

        Assert.Same(fromWizard, auras.Find(new AuraId(906), Wizard));
        Assert.Same(fromHunter, auras.Find(new AuraId(906), null));
        Assert.Null(auras.Find(new AuraId(901), null));
    }

    [Fact]
    public void Record_a_removal_with_no_time_left_and_forget_the_aura()
    {
        var auras = new UnitAuras();
        ActiveAura bleed = Aura(AuraTestData.Bleed(), Hunter);
        auras.Add(bleed, T0);
        auras.ClearChanges();

        Assert.True(auras.Remove(bleed, T0.AddSeconds(2)));
        Assert.False(auras.Remove(bleed, T0.AddSeconds(2)));

        Assert.False(auras.Contains(bleed));
        Assert.Equal(new AuraChange(new AuraId(901), 1, Hunter.RawValue, 1, 0, 12000, AuraChangeKind.Removed),
            Assert.Single(auras.Changes));
    }

    [Fact]
    public void Load_saved_auras_without_owing_the_client_anything()
    {
        int marked = 0;
        var auras = new UnitAuras(() => marked++);

        auras.Load([Aura(AuraTestData.Bleed(), Hunter), Aura(AuraTestData.Fortified(), Wizard)]);

        Assert.Equal(2, auras.Count);
        Assert.Equal([1u, 2u], auras.All.Select(a => a.Key));
        Assert.False(auras.HasChanges);
        Assert.Equal(0, marked);
    }

    [Fact]
    public void Mark_every_change_through_its_callback()
    {
        int marked = 0;
        var auras = new UnitAuras(() => marked++);
        ActiveAura bleed = Aura(AuraTestData.Bleed(), Hunter);

        auras.Add(bleed, T0);
        auras.Changed(bleed, AuraChangeKind.Stacked, T0);
        auras.Remove(bleed, T0);

        Assert.Equal(3, marked);
    }

    [Fact]
    public void Add_up_the_stat_modifiers_of_what_it_holds()
    {
        var auras = new UnitAuras();
        ActiveAura fortified = Aura(AuraTestData.Fortified(), Wizard);

        Assert.True(auras.StatTotals.IsEmpty);
        auras.Add(fortified, T0);
        Assert.Equal(20f, auras.StatTotals.Percent(AuraStat.Armor));
        auras.Remove(fortified, T0);
        Assert.True(auras.StatTotals.IsEmpty);
    }

    [Fact]
    public void Hold_auras_on_characters_and_creatures_only()
    {
        CharacterEntity character = TestCharacters.New(11);
        var creature = new Creature();

        Assert.Same(character.Auras, AuraHolders.Of(character));
        Assert.Same(creature.Auras, AuraHolders.Of(creature));
        Assert.Null(AuraHolders.Of(NSubstitute.Substitute.For<Avalon.World.Public.Units.IUnit>()));
    }

    /// <summary>A refresh is a new application in all but its key: a fresh schedule and no fraction carried over.</summary>
    [Fact]
    public void Start_a_renewed_aura_over_with_no_carry()
    {
        ActiveAura bleed = Aura(AuraTestData.Bleed(), Hunter);
        bleed.PeriodicCarry = 0.75d;
        bleed.Schedule = bleed.Schedule.AfterTicks(2);

        bleed.Renew(AuraTestData.Bleed(), Wizard, AuraSource.None, 2, new AuraSnapshot(7f, 5f, 3), T0.AddSeconds(4));

        Assert.Equal(0d, bleed.PeriodicCarry);
        Assert.Equal(AuraSchedule.Start(T0.AddSeconds(4), 12000, 3000), bleed.Schedule);
        Assert.Equal((Wizard, 2u, T0.AddSeconds(4).UtcDateTime), (bleed.CasterGuid, bleed.Stacks, bleed.AppliedAt));
    }

    /// <summary>Every change to a character's auras marks its save, so the next save rewrites them.</summary>
    [Fact]
    public void Mark_a_characters_save_when_its_auras_change()
    {
        CharacterEntity character = TestCharacters.New(12);

        character.Auras.Add(Aura(AuraTestData.Bleed(), Hunter), T0);

        Assert.True(character.SaveState.AurasDirty);
    }
}
