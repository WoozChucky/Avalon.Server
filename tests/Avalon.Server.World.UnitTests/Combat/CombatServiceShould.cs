using Avalon.World.Combat;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Combat;

public class CombatServiceShould
{
    [Fact]
    public void Should_spawn_encounter_on_first_damage()
    {
        var (svc, reg) = BuildService();
        var attacker = StubCharacter(CharacterClass.Warrior);
        var target   = Substitute.For<ICreature>();
        var ability  = StubAbility(threatMul: 1.0f);

        svc.ApplyDamage(attacker, target, 10, ability);

        Assert.Single(reg.Active);
        var enc = (Encounter)System.Linq.Enumerable.First(reg.Active);
        Assert.Contains(target, enc.Hostiles);
        Assert.Contains(attacker, enc.Players);
    }

    [Fact]
    public void Should_call_OnHit_on_target_with_attacker_and_damage()
    {
        var (svc, _) = BuildService();
        var attacker = StubCharacter(CharacterClass.Warrior);
        var target   = Substitute.For<ICreature>();
        var ability  = StubAbility(1.0f);

        svc.ApplyDamage(attacker, target, 25, ability);

        target.Received(1).OnHit(attacker, 25u);
    }

    [Fact]
    public void Should_mark_attacker_in_combat_when_attacker_is_a_character()
    {
        // MarkCombat exists only on ICharacter (not IUnit / ICreature) — see ICharacter.cs.
        // Combat tag is therefore only applied to character participants.
        var (svc, _) = BuildService();
        var attacker = StubCharacter(CharacterClass.Warrior);
        var target   = Substitute.For<ICreature>();
        var ability  = StubAbility(1.0f);

        svc.ApplyDamage(attacker, target, 10, ability);

        attacker.Received(1).MarkCombat();
    }

    [Fact]
    public void Should_apply_threat_using_class_baseline_and_ability_multiplier()
    {
        // Warrior class threat = 2.0; ability multiplier = 1.5; damage = 10
        // Expected threat = 10 * 1.5 * 2.0 = 30.0 (seed = 0)
        var (svc, reg) = BuildService(initialThreatSeed: 0);
        var attacker = StubCharacter(CharacterClass.Warrior);
        var target   = Substitute.For<ICreature>();
        var ability  = StubAbility(threatMul: 1.5f);

        svc.ApplyDamage(attacker, target, 10, ability);

        var enc = (Encounter)System.Linq.Enumerable.First(reg.Active);
        var threats = enc.GetThreatList(target);
        Assert.Equal(30.0f, threats[attacker], 3);
    }

    [Fact]
    public void Should_use_existing_encounter_when_attacker_already_in_one()
    {
        var (svc, reg) = BuildService();
        var attacker = StubCharacter(CharacterClass.Warrior);
        var t1       = Substitute.For<ICreature>();
        var t2       = Substitute.For<ICreature>();
        var ability  = StubAbility(1.0f);

        svc.ApplyDamage(attacker, t1, 10, ability);
        svc.ApplyDamage(attacker, t2, 10, ability);

        Assert.Single(reg.Active);
        var enc = (Encounter)System.Linq.Enumerable.First(reg.Active);
        Assert.Contains(t1, enc.Hostiles);
        Assert.Contains(t2, enc.Hostiles);
    }

    [Fact]
    public void Should_not_throw_when_target_is_not_a_creature()
    {
        // Damage between two characters (PvP scenario, out of scope V1) — defensive code shouldn't blow up.
        var (svc, _) = BuildService();
        var attacker = StubCharacter(CharacterClass.Warrior);
        var target   = StubCharacter(CharacterClass.Hunter);
        var ability  = StubAbility(1.0f);

        var ex = Record.Exception(() => svc.ApplyDamage(attacker, target, 10, ability));
        Assert.Null(ex);
        target.Received(1).OnHit(attacker, 10u);
    }

    private static (CombatService, EncounterRegistry) BuildService(float initialThreatSeed = 1.0f)
    {
        var cfg = new CombatConfig { InitialThreatSeed = initialThreatSeed };
        var reg = new EncounterRegistry(cfg);
        var svc = new CombatService(cfg, reg);
        return (svc, reg);
    }

    private static ICharacter StubCharacter(CharacterClass cls)
    {
        var c = Substitute.For<ICharacter>();
        c.Class.Returns(cls);
        return c;
    }

    private static IAbility StubAbility(float threatMul)
    {
        var meta = new AbilityMetadata
        {
            Name             = "Test",
            ScriptName       = "x",
            ThreatMultiplier = threatMul,
        };
        var ab = Substitute.For<IAbility>();
        ab.Metadata.Returns(meta);
        return ab;
    }
}
