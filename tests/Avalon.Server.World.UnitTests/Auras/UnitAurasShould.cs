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
    private static readonly DateTimeOffset s_t0 = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly ObjectGuid s_hunter = new(ObjectType.Character, 7);
    private static readonly ObjectGuid s_wizard = new(ObjectType.Character, 8);

    private static ActiveAura Aura(AuraTemplate template, ObjectGuid caster) =>
        new(template, caster, AuraSource.None, 1, new AuraSnapshot(5f, 0f, 1),
            AuraSchedule.Start(s_t0, template.DurationMs, template.TickIntervalMs), template.DurationMs, s_t0.UtcDateTime);

    [Fact]
    public void Key_each_aura_and_record_its_application()
    {
        var auras = new UnitAuras();
        ActiveAura bleed = Aura(AuraTestData.Bleed(), s_hunter);
        ActiveAura burn = Aura(AuraTestData.Burn(), s_wizard);

        auras.Add(bleed, s_t0);
        auras.Add(burn, s_t0.AddSeconds(1));

        Assert.Equal((1u, 2u), (bleed.Key, burn.Key));
        Assert.Equal(2, auras.Count);
        Assert.Equal(
            [new AuraChange(new AuraId(901), 1, s_hunter.RawValue, 1, 12000, 12000, AuraChangeKind.Applied, 0u),
             new AuraChange(new AuraId(902), 2, s_wizard.RawValue, 1, 8000, 9000, AuraChangeKind.Applied, 0u)],
            auras.Changes);
    }

    [Fact]
    public void Find_an_aura_by_id_or_by_id_and_caster()
    {
        var auras = new UnitAuras();
        ActiveAura fromHunter = Aura(AuraTestData.Independent(), s_hunter);
        ActiveAura fromWizard = Aura(AuraTestData.Independent(), s_wizard);
        auras.Add(fromHunter, s_t0);
        auras.Add(fromWizard, s_t0);

        Assert.Same(fromWizard, auras.Find(new AuraId(906), s_wizard));
        Assert.Same(fromHunter, auras.Find(new AuraId(906), null));
        Assert.Null(auras.Find(new AuraId(901), null));
    }

    [Fact]
    public void Record_a_removal_with_no_time_left_and_forget_the_aura()
    {
        var auras = new UnitAuras();
        ActiveAura bleed = Aura(AuraTestData.Bleed(), s_hunter);
        auras.Add(bleed, s_t0);
        auras.ClearChanges();

        Assert.True(auras.Remove(bleed, s_t0.AddSeconds(2)));
        Assert.False(auras.Remove(bleed, s_t0.AddSeconds(2)));

        Assert.False(auras.Contains(bleed));
        Assert.Equal(new AuraChange(new AuraId(901), 1, s_hunter.RawValue, 1, 0, 12000, AuraChangeKind.Removed, 0u),
            Assert.Single(auras.Changes));
    }

    [Fact]
    public void Load_saved_auras_without_owing_the_client_anything()
    {
        int marked = 0;
        var auras = new UnitAuras(() => marked++);

        auras.Load([Aura(AuraTestData.Bleed(), s_hunter), Aura(AuraTestData.Fortified(), s_wizard)]);

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
        ActiveAura bleed = Aura(AuraTestData.Bleed(), s_hunter);

        auras.Add(bleed, s_t0);
        auras.Changed(bleed, AuraChangeKind.Stacked, s_t0);
        auras.Remove(bleed, s_t0);

        Assert.Equal(3, marked);
    }

    [Fact]
    public void Add_up_the_stat_modifiers_of_what_it_holds()
    {
        var auras = new UnitAuras();
        ActiveAura fortified = Aura(AuraTestData.Fortified(), s_wizard);

        Assert.True(auras.StatTotals.IsEmpty);
        auras.Add(fortified, s_t0);
        Assert.Equal(20f, auras.StatTotals.Percent(AuraStat.Armor));
        auras.Remove(fortified, s_t0);
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
        ActiveAura bleed = Aura(AuraTestData.Bleed(), s_hunter);
        bleed.PeriodicCarry = 0.75d;
        bleed.Schedule = bleed.Schedule.AfterTicks(2);

        bleed.Renew(AuraTestData.Bleed(), s_wizard, AuraSource.None, 2, new AuraSnapshot(7f, 5f, 3), 12000,
            s_t0.AddSeconds(4));

        Assert.Equal(0d, bleed.PeriodicCarry);
        Assert.Equal(AuraSchedule.Start(s_t0.AddSeconds(4), 12000, 3000), bleed.Schedule);
        Assert.Equal((s_wizard, 2u, s_t0.AddSeconds(4).UtcDateTime), (bleed.CasterGuid, bleed.Stacks, bleed.AppliedAt));
    }

    /// <summary>
    /// The crowd-control cache the movement, cast and AI gates read: the union of what the held auras grant, kept
    /// through every change, a refresh onto a reloaded template that no longer grants anything (through the aura system,
    /// which renews the held copy before it reports the change), and select's Load. Each step reads the gates: (state,
    /// move, turn, act, cast an ability, cast the basic attack).
    /// </summary>
    [Fact]
    public void Track_crowd_control_through_add_refresh_remove_and_load()
    {
        var h = new AuraHarness();
        h.Use(Controlling(912, CrowdControl.Stun), Controlling(913, CrowdControl.Root));
        CharacterEntity unit = h.Player(13);
        UnitAuras auras = unit.Auras;
        Assert.Equal((CrowdControl.None, true, true, true, true, true), Gates(unit));

        h.Auras.Apply(null, unit, new AuraId(912), AuraSource.None);
        Assert.Equal((CrowdControl.Stun, false, false, false, false, false), Gates(unit));
        h.Auras.Apply(null, unit, new AuraId(913), AuraSource.None);
        Assert.Equal((CrowdControl.Stun | CrowdControl.Root, false, false, false, false, false), Gates(unit));
        h.Auras.Remove(unit, new AuraId(912), AuraRemoveReason.Cancelled);
        Assert.Equal((CrowdControl.Root, false, true, true, true, true), Gates(unit));

        // A reload cleared the root's Control; the next application refreshes the held copy onto that template.
        h.Use(Controlling(913, CrowdControl.None));
        Assert.Equal(AuraApplyResult.Refreshed, h.Auras.Apply(null, unit, new AuraId(913), AuraSource.None));
        Assert.Equal((CrowdControl.None, true, true, true, true, true), Gates(unit));

        ActiveAura silence = Aura(Controlling(915, CrowdControl.Silence), s_hunter);
        auras.Load([Aura(AuraTestData.Bleed(), s_hunter), silence]);
        Assert.Equal((CrowdControl.Silence, true, true, true, false, true), Gates(unit));
        auras.Remove(silence, s_t0);
        Assert.Equal((CrowdControl.None, true, true, true, true, true), Gates(unit));
    }

    private static (CrowdControl, bool, bool, bool, bool, bool) Gates(CharacterEntity unit) =>
        (ControlGates.Of(unit), ControlGates.CanMove(unit), ControlGates.CanTurn(unit), ControlGates.CanAct(unit),
            ControlGates.CanCast(unit, basic: false), ControlGates.CanCast(unit, basic: true));

    private static AuraTemplate Controlling(uint id, CrowdControl control) => new()
    {
        Id = new AuraId(id),
        Name = "Controlled",
        Icon = "controlled",
        Kind = AuraKind.Harmful,
        DurationMs = 3000,
        Stacking = AuraStacking.Refresh,
        MaxStacks = 1,
        Control = control,
    };

    /// <summary>Every change to a character's auras marks its save, so the next save rewrites them.</summary>
    [Fact]
    public void Mark_a_characters_save_when_its_auras_change()
    {
        CharacterEntity character = TestCharacters.New(12);

        character.Auras.Add(Aura(AuraTestData.Bleed(), s_hunter), s_t0);

        Assert.True(character.SaveState.AurasDirty);
    }
}
