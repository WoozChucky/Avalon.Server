using Avalon.Combat;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Auras;
using Avalon.World.Auras;
using Avalon.World.Entities;
using Avalon.World.Scripts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Auras;

/// <summary>Every hook in its place, every hook contained, and a script ending its own aura.</summary>
[Collection(nameof(RecordingAuraScript))]
public class AuraScriptHooksShould
{
    private readonly AuraHarness _h;

    public AuraScriptHooksShould()
    {
        var manager = Substitute.For<IScriptManager>();
        manager.GetAuraScript(Arg.Any<string>()).Returns(call => AuraHarness.TestScript(call.Arg<string>()));
        var host = new AuraScripts(manager, new ServiceCollection().BuildServiceProvider(), TimeProvider.System,
            NullLogger<AuraScripts>.Instance);
        _h = new AuraHarness(scripts: host);
        _h.Use(AuraTestData.Scripted(AuraTestData.Bleed(), nameof(RecordingAuraScript)),
            AuraTestData.Scripted(AuraTestData.Burn(), nameof(ThrowingAuraScript)),
            AuraTestData.Scripted(AuraTestData.Independent(), nameof(EndOnTickAuraScript)),
            Named(AuraTestData.Independent(907), "Doom", nameof(KillOnTickAuraScript)),
            Named(AuraTestData.Independent(908), "Fleeting", nameof(EndOnTickRecordingAuraScript)),
            Named(AuraTestData.Independent(909), "Stillborn", nameof(EndOnApplyAuraScript)),
            Named(AuraTestData.Independent(910), "Ruin", nameof(KillOnApplyAuraScript)),
            Named(AuraTestData.Independent(911), "Undertow", nameof(KillOnRemoveAuraScript)),
            Named(AuraTestData.Independent(912), "Echo", nameof(RemoveOnRemoveAuraScript)),
            Named(AuraTestData.Independent(913), "Keepsake", nameof(KeepContextAuraScript)),
            Named(AuraTestData.Renew(914), "Mend", nameof(RecordingAuraScript)),
            AuraTestData.Scripted(AuraTestData.Fortified(), nameof(ThrowingAuraScript)));
        RecordingAuraScript.Heard.Clear();
        KeepContextAuraScript.Kept = null;
    }

    private static AuraTemplate Named(AuraTemplate template, string name, string script)
    {
        template.Name = name;
        return AuraTestData.Scripted(template, script);
    }

    [Fact]
    public void Hear_apply_stack_tick_and_remove_in_order()
    {
        CharacterEntity warrior = _h.Player(911_101);
        Creature boar = _h.Creature(911_901);

        _h.Auras.Apply(warrior, boar, new AuraId(901), AuraSource.None);
        _h.Auras.Apply(warrior, boar, new AuraId(901), AuraSource.None);
        _h.Advance(TimeSpan.FromSeconds(3));
        _h.Auras.Update();
        _h.Auras.RemoveAll(boar, AuraRemoveReason.Death);

        Assert.Equal(["Bleed:apply:1", "Bleed:stack:2", "Bleed:tick", "Bleed:remove:Death"], RecordingAuraScript.Heard);
    }

    [Fact]
    public void Hear_a_cancel_as_Cancelled()
    {
        CharacterEntity healer = _h.Player(911_120);

        _h.Auras.Apply(healer, healer, new AuraId(914), AuraSource.None);
        Assert.Equal(AuraCancelResult.Ok, _h.Auras.Cancel(healer, new AuraId(914)));

        Assert.Equal(["Mend:apply:1", "Mend:remove:Cancelled"], RecordingAuraScript.Heard);
    }

    [Fact]
    public void Keep_the_aura_going_when_its_script_throws()
    {
        Creature boar = _h.Creature(911_902);

        Assert.Equal(AuraApplyResult.Applied, _h.Auras.Apply(_h.Player(911_102), boar, new AuraId(902), AuraSource.None));
        _h.Advance(TimeSpan.FromSeconds(3));
        _h.Auras.Update();
        Assert.Equal(1000u - 8u, boar.CurrentHealth);   // 24 over 3 ticks
        _h.Auras.RemoveAll(boar, AuraRemoveReason.Death);

        Assert.Equal(0, boar.Auras.Count);
    }

    [Fact]
    public void End_an_aura_its_script_removes_after_the_hook()
    {
        Creature boar = _h.Creature(911_903);
        _h.Auras.Apply(_h.Player(911_103), boar, new AuraId(906), AuraSource.None);

        _h.Advance(TimeSpan.FromSeconds(6));
        _h.Auras.Update();

        Assert.Equal(1000u - 2u, boar.CurrentHealth);   // the first tick landed, the second never came
        Assert.Equal(0, boar.Auras.Count);
    }

    [Fact]
    public void Tell_a_script_once_that_its_own_tick_ended_it()
    {
        Creature boar = _h.Creature(911_905);
        _h.Auras.Apply(_h.Player(911_105), boar, new AuraId(908), AuraSource.None);

        _h.Advance(TimeSpan.FromSeconds(6));
        _h.Auras.Update();
        _h.Auras.RemoveAll(boar, AuraRemoveReason.Death);

        Assert.Equal(["Fleeting:tick", "Fleeting:remove:Script"], RecordingAuraScript.Heard);
        Assert.Equal(0, boar.Auras.Count);
    }

    [Fact]
    public void End_every_aura_once_when_a_script_kills_from_its_tick()
    {
        CharacterEntity warrior = _h.Player(911_106);
        Creature boar = _h.Creature(911_906);
        _h.Auras.Apply(warrior, boar, new AuraId(907), AuraSource.None);
        _h.Auras.Apply(warrior, boar, new AuraId(901), AuraSource.None);

        _h.Advance(TimeSpan.FromSeconds(3));
        _h.Auras.Update();

        Assert.Equal(0u, boar.CurrentHealth);
        Assert.Equal(0, boar.Auras.Count);
        // The killing tick came first: the bleed after it never ticked, and each aura ended once.
        Assert.Equal(["Bleed:apply:1", "Doom:tick", "Doom:remove:Death", "Bleed:remove:Death"], RecordingAuraScript.Heard);
    }

    [Fact]
    public void End_an_aura_its_script_removes_from_its_first_hook()
    {
        Creature boar = _h.Creature(911_907);

        _h.Auras.Apply(_h.Player(911_107), boar, new AuraId(909), AuraSource.None);

        Assert.Equal(0, boar.Auras.Count);
        Assert.Equal(["Stillborn:remove:Script"], RecordingAuraScript.Heard);
    }

    [Fact]
    public void Run_no_hook_for_an_aura_without_a_script()
    {
        var harness = new AuraHarness(scripts: new AuraScripts(Substitute.For<IScriptManager>(),
            new ServiceCollection().BuildServiceProvider(), TimeProvider.System, NullLogger<AuraScripts>.Instance));
        harness.Use(AuraTestData.Bleed());
        Creature boar = harness.Creature(911_908);

        harness.Auras.Apply(harness.Player(911_108), boar, new AuraId(901), AuraSource.None);
        harness.Advance(TimeSpan.FromSeconds(12));
        harness.Auras.Update();

        Assert.Equal(0, boar.Auras.Count);
        Assert.Empty(RecordingAuraScript.Heard);
    }

    [Fact]
    public void End_every_aura_and_enter_no_combat_when_a_first_hook_kills()
    {
        CharacterEntity caster = _h.Player(911_110);
        _h.Characters.Remove(caster.Guid);   // not in the instance, so the hook's damage credits nobody
        CharacterEntity victim = _h.Player(911_111);

        _h.Auras.Apply(caster, victim, new AuraId(910), AuraSource.None);

        Assert.True(victim.IsDead);
        Assert.Equal(0, victim.Auras.Count);
        Assert.Equal(["Ruin:remove:Death"], RecordingAuraScript.Heard);
        Assert.Null(_h.Encounters.FindEncounterContaining(victim));
        Assert.Null(_h.Encounters.FindEncounterContaining(caster));
        Assert.False(caster.IsInCombat);
    }

    [Fact]
    public void Tick_nothing_on_a_corpse_an_ending_script_made()
    {
        CharacterEntity warrior = _h.Player(911_112);
        Creature boar = _h.Creature(911_912);
        _h.Auras.Apply(warrior, boar, new AuraId(911), AuraSource.None);
        _h.Auras.Apply(warrior, boar, new AuraId(901), AuraSource.None);

        _h.Advance(TimeSpan.FromSeconds(3));
        _h.Auras.Update();

        Assert.Equal(0u, boar.CurrentHealth);
        Assert.Equal(0, boar.Auras.Count);
        // The bleed after the aura whose removal killed never ticked, and ended once, with the death.
        Assert.Equal(["Bleed:apply:1", "Undertow:remove:Script", "Bleed:remove:Death"], RecordingAuraScript.Heard);
    }

    [Fact]
    public void Ignore_a_removal_asked_for_from_the_removal_itself()
    {
        Creature boar = _h.Creature(911_913);
        _h.Auras.Apply(_h.Player(911_113), boar, new AuraId(912), AuraSource.None);

        _h.Auras.RemoveAll(boar, AuraRemoveReason.Death);
        _h.Advance(TimeSpan.FromSeconds(6));
        _h.Auras.Update();
        _h.Auras.RemoveAll(boar, AuraRemoveReason.Death);

        Assert.Equal(0, boar.Auras.Count);
        Assert.Equal(["Echo:remove:Death"], RecordingAuraScript.Heard);
    }

    [Fact]
    public void Let_a_kept_context_do_nothing_once_its_hook_returned()
    {
        Creature boar = _h.Creature(911_914);
        _h.Auras.Apply(_h.Player(911_114), boar, new AuraId(913), AuraSource.None);
        ActiveAura keepsake = Assert.Single(boar.Auras.All);
        IAuraContext kept = Assert.IsAssignableFrom<IAuraContext>(KeepContextAuraScript.Kept);
        boar.CurrentHealth = 900;

        Assert.Equal(0u, kept.Damage(50));
        Assert.Equal(0u, kept.Heal(50));
        kept.Remove();
        kept.Tell("too late");

        Assert.Equal(900u, boar.CurrentHealth);
        Assert.False(keepsake.ScriptEnded);
        Assert.Single(boar.Auras.All);
    }

    [Fact]
    public void Apply_an_aura_and_its_stats_when_its_first_hook_throws()
    {
        Creature boar = _h.Creature(911_915, armor: 100);

        Assert.Equal(AuraApplyResult.Applied, _h.Auras.Apply(_h.Player(911_115), boar, new AuraId(905), AuraSource.None));

        Assert.Single(boar.Auras.All);
        Assert.Equal(new DefenderCombat(120, 0f, 0f), Defence(boar));   // +20 % armour
    }

    /// <summary>A creature's World-side defence, read as AuraStatsIntegrationShould reads it.</summary>
    private static DefenderCombat Defence(Creature creature) =>
        (DefenderCombat)typeof(Creature).GetProperty("Defence",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(creature)!;

    [Fact]
    public void Show_a_script_the_aura_it_runs_for()
    {
        CharacterEntity warrior = _h.Player(911_104);
        Creature boar = _h.Creature(911_904);
        _h.Auras.Apply(warrior, boar, new AuraId(901), AuraSource.None);
        ActiveAura bleed = Assert.Single(boar.Auras.All);

        var ctx = new AuraContext(_h.Auras, boar, bleed, AuraHarness.T0.AddSeconds(2));

        Assert.Equal(("Bleed", boar.Guid, warrior.Guid, true, 1u), (ctx.AuraName, ctx.Target, ctx.Caster, ctx.CasterPresent, ctx.Stacks));
        Assert.Equal((TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(12)), (ctx.Remaining, ctx.Duration));
        Assert.Equal((1000u, 1000u, false), (ctx.TargetHealth, ctx.TargetMaxHealth, ctx.TargetIsCharacter));
        Assert.Equal(7u, ctx.Damage(7));
        Assert.Equal(993u, boar.CurrentHealth);
        Assert.Equal(5u, ctx.Heal(5));
        Assert.Equal(998u, boar.CurrentHealth);
        ctx.Remove();
        Assert.True(bleed.ScriptEnded);
    }

    [Fact]
    public void Credit_no_caster_once_it_is_gone()
    {
        CharacterEntity warrior = _h.Player(911_109);
        Creature boar = _h.Creature(911_909);
        _h.Auras.Apply(warrior, boar, new AuraId(901), AuraSource.None);
        ActiveAura bleed = Assert.Single(boar.Auras.All);
        _h.Characters.Remove(warrior.Guid);

        var ctx = new AuraContext(_h.Auras, boar, bleed, AuraHarness.T0);

        Assert.False(ctx.CasterPresent);
        Assert.Equal(7u, ctx.Damage(7));
        Assert.Equal(993u, boar.CurrentHealth);
    }
}

[CollectionDefinition(nameof(RecordingAuraScript), DisableParallelization = true)]
public sealed class RecordingAuraScriptCollection;
