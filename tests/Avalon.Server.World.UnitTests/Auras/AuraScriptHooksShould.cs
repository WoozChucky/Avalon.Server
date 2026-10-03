using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Auras;
using Avalon.World.Entities;
using Avalon.World.Scripts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Auras;

/// <summary>Every hook in its place, every hook contained, and a script ending its own aura.</summary>
[Collection(nameof(RecordingAuraScript))]
public class AuraScriptHooksShould
{
    private static readonly Type[] TestScripts =
    [
        typeof(RecordingAuraScript), typeof(ThrowingAuraScript), typeof(EndOnTickAuraScript), typeof(KillOnTickAuraScript),
        typeof(EndOnTickRecordingAuraScript), typeof(EndOnApplyAuraScript),
    ];

    private readonly AuraHarness _h;

    public AuraScriptHooksShould()
    {
        var manager = Substitute.For<IScriptManager>();
        foreach (Type type in TestScripts)
            manager.GetAuraScript(type.Name).Returns(type);
        var host = new AuraScripts(manager, new ServiceCollection().BuildServiceProvider(), TimeProvider.System,
            NullLogger<AuraScripts>.Instance);
        _h = new AuraHarness(scripts: host);
        _h.Use(AuraTestData.Scripted(AuraTestData.Bleed(), nameof(RecordingAuraScript)),
            AuraTestData.Scripted(AuraTestData.Burn(), nameof(ThrowingAuraScript)),
            AuraTestData.Scripted(AuraTestData.Independent(), nameof(EndOnTickAuraScript)),
            Named(AuraTestData.Independent(907), "Doom", nameof(KillOnTickAuraScript)),
            Named(AuraTestData.Independent(908), "Fleeting", nameof(EndOnTickRecordingAuraScript)),
            Named(AuraTestData.Independent(909), "Stillborn", nameof(EndOnApplyAuraScript)));
        RecordingAuraScript.Heard.Clear();
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
