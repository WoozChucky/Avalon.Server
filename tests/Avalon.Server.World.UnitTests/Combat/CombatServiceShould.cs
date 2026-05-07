using System;
using Avalon.World.Combat;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Units;
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
        var target   = StubCreature();
        var ability  = StubAbility(threatMul: 1.0f);

        svc.ApplyDamage(attacker, target, 10, ability);

        Assert.Single(reg.Active);
        var enc = (Encounter)System.Linq.Enumerable.First(reg.Active);
        Assert.Contains(target, enc.Hostiles);
        Assert.Contains(attacker, enc.Players);
    }

    [Fact]
    public void Should_populate_encounter_when_creature_attacks_character()
    {
        // Regression: ResolveOrSpawn must classify by TYPE, not by ROLE.
        // mob-attacks-player is the primary combat scenario and must populate the encounter.
        var (svc, reg) = BuildService();
        var attacker = StubCreature();
        var target   = StubCharacter(CharacterClass.Warrior);
        var ability  = StubAbility(threatMul: 1.0f);

        svc.ApplyDamage(attacker, target, 10, ability);

        Assert.Single(reg.Active);
        var enc = (Encounter)System.Linq.Enumerable.First(reg.Active);
        Assert.Contains(attacker, enc.Hostiles);
        Assert.Contains(target,   enc.Players);
    }

    [Fact]
    public void Should_call_OnHit_on_target_with_attacker_and_damage()
    {
        var (svc, _) = BuildService();
        var attacker = StubCharacter(CharacterClass.Warrior);
        var target   = StubCreature();
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
        var target   = StubCreature();
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
        var target   = StubCreature();
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
        var t1       = StubCreature();
        var t2       = StubCreature();
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

    [Fact]
    public void Should_spawn_encounter_on_aggro_range_entry()
    {
        var (svc, reg) = BuildService();
        var hostile = StubCreature();
        var player  = StubCharacter(CharacterClass.Hunter);

        svc.EnterCombat(hostile, player);

        Assert.Single(reg.Active);
        var enc = (Encounter)System.Linq.Enumerable.First(reg.Active);
        Assert.Contains(hostile, enc.Hostiles);
        Assert.Contains(player,  enc.Players);
        Assert.Equal(1.0f, enc.GetThreatList(hostile)[player]);
    }

    [Fact]
    public void Should_use_existing_encounter_when_aggro_added_to_already_engaged_pack()
    {
        var (svc, reg) = BuildService();
        var h1 = StubCreature();
        var h2 = StubCreature();
        var player = StubCharacter(CharacterClass.Hunter);
        var ability = StubAbility(1.0f);

        svc.ApplyDamage(player, h1, 10, ability);
        svc.EnterCombat(h2, player);   // h2 wanders into aggro range during fight

        Assert.Single(reg.Active);
        var enc = (Encounter)System.Linq.Enumerable.First(reg.Active);
        Assert.Contains(h1, enc.Hostiles);
        Assert.Contains(h2, enc.Hostiles);
    }

    [Fact]
    public void Should_merge_when_attacker_in_existing_encounter_attacks_new_hostile()
    {
        var (svc, reg) = BuildService();
        var attacker = StubCharacter(CharacterClass.Warrior);
        var h1 = StubCreature();
        var h2 = StubCreature();
        var ability = StubAbility(1.0f);

        svc.ApplyDamage(attacker, h1, 10, ability);
        svc.ApplyDamage(attacker, h2, 10, ability);

        Assert.Single(reg.Active);
        var enc = (Encounter)System.Linq.Enumerable.First(reg.Active);
        Assert.Contains(h1, enc.Hostiles);
        Assert.Contains(h2, enc.Hostiles);
    }

    [Fact]
    public void Should_keep_separate_encounters_when_neutral_pack_not_attacked()
    {
        var (svc, reg) = BuildService();
        var p   = StubCharacter(CharacterClass.Warrior);
        var h1  = StubCreature();
        var h2  = StubCreature();   // neutral pack — never attacked
        var ab  = StubAbility(1.0f);

        svc.ApplyDamage(p, h1, 10, ab);
        // h2 never engaged → no encounter for it

        Assert.Single(reg.Active);
        var enc = (Encounter)System.Linq.Enumerable.First(reg.Active);
        Assert.DoesNotContain(h2, enc.Hostiles);
    }

    [Fact]
    public void Should_cap_merge_at_50_hostile_participants()
    {
        var (svc, reg) = BuildService();
        var attacker = StubCharacter(CharacterClass.Warrior);
        var ability  = StubAbility(1.0f);

        for (int i = 0; i < 51; i++)
        {
            var h = StubCreature();
            svc.ApplyDamage(attacker, h, 1, ability);
        }

        // Expect 2 encounters: first capped at 50, the 51st spawns a new encounter.
        Assert.Equal(2, reg.Active.Count);
        var encounters = System.Linq.Enumerable.ToList(reg.Active);
        int firstCount  = encounters[0].Hostiles.Count;
        int secondCount = encounters[1].Hostiles.Count;
        Assert.True(firstCount + secondCount == 51);
        Assert.True(firstCount == 50 || secondCount == 50);
        Assert.True(firstCount == 1  || secondCount == 1);
    }

    [Fact]
    public void Should_split_heal_threat_evenly_across_hostiles_in_encounter()
    {
        var (svc, reg) = BuildService(initialThreatSeed: 0);
        var healer = StubCharacter(CharacterClass.Healer);
        var ally   = StubCharacter(CharacterClass.Warrior);
        var hostile1 = StubCreature();
        var hostile2 = StubCreature();
        var healAbility = Substitute.For<IAbility>();
        healAbility.Metadata.Returns(new AbilityMetadata { Name = "H", ScriptName = "h", HealThreatPerHp = 0.5f });

        // Set up encounter: ally is engaged with both hostiles.
        svc.EnterCombat(hostile1, ally);
        svc.EnterCombat(hostile2, ally);

        // Now healer heals ally for 100. Heal-threat = 100 * 0.5 * 1.0 = 50, split across 2 hostiles = 25 each.
        // (Initial threat seed is 0 in this test, so the seeded entry from EnterCombat is 0, then +25 from heal.)
        svc.ApplyHeal(healer, ally, 100, healAbility);

        var enc = (Encounter)System.Linq.Enumerable.First(reg.Active);
        Assert.Equal(25.0f, enc.GetThreatList(hostile1)[healer], 3);
        Assert.Equal(25.0f, enc.GetThreatList(hostile2)[healer], 3);
    }

    [Fact]
    public void Should_not_generate_heal_threat_when_target_not_in_encounter()
    {
        var (svc, reg) = BuildService();
        var healer = StubCharacter(CharacterClass.Healer);
        var ally   = StubCharacter(CharacterClass.Warrior);
        var ab = Substitute.For<IAbility>();
        ab.Metadata.Returns(new AbilityMetadata { Name = "H", ScriptName = "h", HealThreatPerHp = 0.5f });

        svc.ApplyHeal(healer, ally, 100, ab);

        Assert.Empty(reg.Active);
    }

    [Fact]
    public void Should_skip_heal_threat_when_HealThreatPerHp_is_zero()
    {
        var (svc, reg) = BuildService(initialThreatSeed: 0);
        var healer = StubCharacter(CharacterClass.Healer);
        var ally   = StubCharacter(CharacterClass.Warrior);
        var hostile = StubCreature();
        var healAbility = Substitute.For<IAbility>();
        healAbility.Metadata.Returns(new AbilityMetadata { Name = "H", ScriptName = "h", HealThreatPerHp = 0.0f });

        svc.EnterCombat(hostile, ally);
        svc.ApplyHeal(healer, ally, 100, healAbility);

        var enc = (Encounter)System.Linq.Enumerable.First(reg.Active);
        var threats = enc.GetThreatList(hostile);
        Assert.False(threats.ContainsKey(healer));
    }

    [Fact]
    public void Should_set_taunt_caster_above_top_threat()
    {
        var (svc, reg) = BuildService(initialThreatSeed: 0);
        var hostile = StubCreature();
        var dpsCharacter = StubCharacter(CharacterClass.Hunter);
        var tankCharacter = StubCharacter(CharacterClass.Warrior);
        var dmgAbility = StubAbility(1.0f);

        // DPS deals damage and pulls aggro.
        svc.ApplyDamage(dpsCharacter, hostile, 100, dmgAbility);

        float beforeTaunt = ((Encounter)System.Linq.Enumerable.First(reg.Active))
                            .GetThreatList(hostile)[dpsCharacter];

        // Tank taunts.
        svc.ApplyTaunt(tankCharacter, hostile, 5000);

        var enc = (Encounter)System.Linq.Enumerable.First(reg.Active);
        var threats = enc.GetThreatList(hostile);
        Assert.True(threats[tankCharacter] > beforeTaunt);
        Assert.True(threats[tankCharacter] > threats[dpsCharacter]);
        Assert.Same(tankCharacter, hostile.TauntedBy);
    }

    [Fact]
    public void Should_set_taunt_expires_at_in_future()
    {
        var (svc, _) = BuildService();
        var hostile = StubCreature();
        var caster  = StubCharacter(CharacterClass.Warrior);
        var dmgAb = StubAbility(1.0f);

        svc.ApplyDamage(caster, hostile, 1, dmgAb);   // ensure encounter exists
        var before = System.DateTime.UtcNow;
        svc.ApplyTaunt(caster, hostile, 5000);

        Assert.True(hostile.TauntExpiresAt >= before.AddMilliseconds(4900));
        Assert.True(hostile.TauntExpiresAt <= before.AddMilliseconds(5100));
    }

    [Fact]
    public void Should_noop_taunt_when_target_not_in_encounter()
    {
        var (svc, reg) = BuildService();
        var hostile = StubCreature();
        var caster  = StubCharacter(CharacterClass.Warrior);

        svc.ApplyTaunt(caster, hostile, 5000);

        Assert.Empty(reg.Active);
        Assert.Null(hostile.TauntedBy);
    }

    [Fact]
    public void Should_dispose_encounter_when_ended()
    {
        var cfg = new CombatConfig { InitialThreatSeed = 0, EncounterEndGraceSeconds = 0.05f };
        var reg = new EncounterRegistry(cfg);
        var ctx = Substitute.For<ISimulationContext>();
        var svc = new CombatService(cfg, reg, ctx);

        var attacker = StubCharacter(CharacterClass.Warrior);
        var target   = StubCreature();
        target.Position.Returns(default(Avalon.Common.Mathematics.Vector3));
        var ability  = StubAbility(1.0f);

        svc.ApplyDamage(attacker, target, 10, ability);
        var enc = (Encounter)System.Linq.Enumerable.First(reg.Active);
        enc.OnParticipantDied(target);

        System.Threading.Thread.Sleep(60);   // exceed 50ms grace
        svc.Update(TimeSpan.FromMilliseconds(10));

        Assert.Empty(reg.Active);
    }

    [Fact]
    public void Should_broadcast_death_when_lethal_hit_kills_creature()
    {
        // Lethal blow — creature's CurrentHealth is 0 after OnHit. CombatService should
        // call OnParticipantDied and broadcast SUnitDeathPacket via the simulation context.
        var (svc, _, ctx) = BuildServiceWithContext();
        var attacker = StubCharacter(CharacterClass.Warrior);
        var target   = StubCreature();
        // Substitute creature: simulate the script's lethal-hit behaviour by returning 0 HP after OnHit.
        target.CurrentHealth.Returns(0u);
        var ab = StubAbility(1.0f);

        svc.ApplyDamage(attacker, target, 9999u, ab);

        ctx.Received(1).BroadcastUnitDeath(target, attacker);
    }

    [Fact]
    public void Should_broadcast_death_when_character_isdead_after_hit()
    {
        // Character death path — IsDead flag, not CurrentHealth==0 alone.
        var (svc, _, ctx) = BuildServiceWithContext();
        var attacker = StubCreature();
        var target   = StubCharacter(CharacterClass.Hunter);
        target.IsDead.Returns(true);   // simulate post-OnHit death state
        var ab = StubAbility(1.0f);

        svc.ApplyDamage(attacker, target, 9999u, ab);

        ctx.Received(1).BroadcastUnitDeath(target, attacker);
    }

    [Fact]
    public void Should_not_broadcast_death_when_target_survives()
    {
        var (svc, _, ctx) = BuildServiceWithContext();
        var attacker = StubCharacter(CharacterClass.Warrior);
        var target   = StubCreature();
        target.CurrentHealth.Returns(50u);   // alive after the hit
        var ab = StubAbility(1.0f);

        svc.ApplyDamage(attacker, target, 10u, ab);

        ctx.DidNotReceive().BroadcastUnitDeath(Arg.Any<IUnit>(), Arg.Any<IUnit>());
    }

    [Fact]
    public void Should_route_raw_damage_through_apply_damage()
    {
        // Raw-damage overload — used by CreatureCombatScript for melee swings (no IAbility).
        var (svc, reg, _) = BuildServiceWithContext();
        var attacker = StubCreature();
        var target   = StubCharacter(CharacterClass.Warrior);
        target.CurrentHealth.Returns(100u);   // alive

        svc.ApplyDamage(attacker, target, 5u);

        target.Received(1).OnHit(attacker, 5u);
        Assert.Single(reg.Active);
    }

    [Fact]
    public void Should_apply_default_threat_multiplier_on_raw_damage()
    {
        // Warrior class threat = 2.0; default multiplier = 1.0; damage = 10 → 20.0 (seed = 0).
        var (svc, reg, _) = BuildServiceWithContext(initialThreatSeed: 0);
        var attacker = StubCharacter(CharacterClass.Warrior);
        var target   = StubCreature();
        target.CurrentHealth.Returns(50u);

        svc.ApplyDamage(attacker, target, 10u);

        var enc = (Encounter)System.Linq.Enumerable.First(reg.Active);
        Assert.Equal(20.0f, enc.GetThreatList(target)[attacker], 3);
    }

    private static (CombatService, EncounterRegistry) BuildService(float initialThreatSeed = 1.0f)
    {
        var (svc, reg, _) = BuildServiceWithContext(initialThreatSeed);
        return (svc, reg);
    }

    private static (CombatService, EncounterRegistry, ISimulationContext) BuildServiceWithContext(float initialThreatSeed = 1.0f)
    {
        var cfg = new CombatConfig { InitialThreatSeed = initialThreatSeed };
        var reg = new EncounterRegistry(cfg);
        var ctx = Substitute.For<ISimulationContext>();
        var svc = new CombatService(cfg, reg, ctx);
        return (svc, reg, ctx);
    }

    private static ICharacter StubCharacter(CharacterClass cls)
    {
        var c = Substitute.For<ICharacter>();
        c.Class.Returns(cls);
        // CurrentHealth defaults to 0u for value-type substitutes, which would trip the new
        // death-detection check in CombatService. Default to "alive" so existing tests stay green;
        // tests that exercise the death path explicitly opt in via IsDead.Returns(true).
        c.CurrentHealth.Returns(100u);
        return c;
    }

    private static ICreature StubCreature()
    {
        // NSubstitute auto-substitutes reference-type property reads. Initialize TauntedBy via
        // its setter so the substitute remembers the value (null) instead of returning an auto-sub.
        // Subsequent setter calls by the service-under-test will overwrite this stored value.
        // CurrentHealth defaults to 0u for value-type substitutes — that would trigger the new
        // death-detection path in CombatService and remove the creature from the encounter
        // unintentionally. Default to "alive" (positive HP) so existing tests stay green; tests
        // that exercise the death path explicitly set CurrentHealth back to 0.
        var c = Substitute.For<ICreature>();
        c.TauntedBy = null;
        c.CurrentHealth.Returns(100u);
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
