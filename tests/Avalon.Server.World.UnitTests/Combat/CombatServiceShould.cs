using System;
using System.Linq;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Avalon.World.Scripts.Creatures;
using Microsoft.Extensions.Logging.Abstractions;
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
        ally.Health.Returns(300u); // room for the whole 100: heal threat counts only what is restored (#531)
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
        ally.Health.Returns(300u); // room to restore, so the heal reaches the guard this test is named for
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
        ally.Health.Returns(300u); // room to restore, so the heal reaches the guard this test is named for
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
    public void Should_not_lower_health_when_healing_a_unit_above_its_maximum()
    {
        // A unit can sit above its maximum (equipment removed, a buff that raised it dropped).
        // A heal must never take health away: 120 of 100 stays 120, restores 0 and adds no threat (#548).
        var (svc, reg) = BuildService(initialThreatSeed: 0);
        var healer = StubCharacter(CharacterClass.Healer);
        var ally   = StubCharacter(CharacterClass.Warrior);
        ally.Health.Returns(100u);
        ally.CurrentHealth.Returns(120u);
        var hostile = StubCreature();
        var healAbility = Substitute.For<IAbility>();
        healAbility.Metadata.Returns(new AbilityMetadata { Name = "H", ScriptName = "h", HealThreatPerHp = 0.5f });

        svc.EnterCombat(hostile, ally);
        svc.ApplyHeal(healer, ally, 50, healAbility);

        ally.DidNotReceive().CurrentHealth = Arg.Is<uint>(v => v < 120u);
        var enc = (Encounter)System.Linq.Enumerable.First(reg.Active);
        Assert.False(enc.GetThreatList(hostile).ContainsKey(healer));
    }

    [Fact]
    public void Should_cap_a_heal_at_maximum_health()
    {
        var (svc, _) = BuildService();
        var healer = StubCharacter(CharacterClass.Healer);
        var ally   = StubCharacter(CharacterClass.Warrior);
        ally.Health.Returns(100u);
        ally.CurrentHealth.Returns(90u);
        var healAbility = Substitute.For<IAbility>();
        healAbility.Metadata.Returns(new AbilityMetadata { Name = "H", ScriptName = "h", HealThreatPerHp = 0.5f });

        svc.ApplyHeal(healer, ally, 50, healAbility);

        ally.Received().CurrentHealth = 100u;
        ally.DidNotReceive().CurrentHealth = Arg.Is<uint>(v => v != 100u);
    }

    [Fact]
    public void Should_leave_a_unit_at_exactly_maximum_health_where_it_is()
    {
        var (svc, _) = BuildService();
        var healer = StubCharacter(CharacterClass.Healer);
        var ally   = StubCharacter(CharacterClass.Warrior);
        ally.Health.Returns(100u);
        ally.CurrentHealth.Returns(100u);
        var healAbility = Substitute.For<IAbility>();
        healAbility.Metadata.Returns(new AbilityMetadata { Name = "H", ScriptName = "h", HealThreatPerHp = 0.5f });

        svc.ApplyHeal(healer, ally, 50, healAbility);

        ally.DidNotReceive().CurrentHealth = Arg.Is<uint>(v => v != 100u);
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
        // It is alive before the hit, since a corpse takes no hits (#588).
        target.WhenForAnyArgs(t => t.OnHit(default!, default)).Do(_ => target.CurrentHealth.Returns(0u));
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
    public void Should_revive_player_at_configured_health_fraction_and_broadcast()
    {
        // RevivePlayer clears IsDead, sets CurrentHealth to MaxHealth * ReviveHealthFraction,
        // moves the character to the supplied position, and broadcasts SUnitRevivePacket.
        var cfg = new CombatConfig { ReviveHealthFraction = 0.25f };
        var reg = new EncounterRegistry(cfg);
        var ctx = Substitute.For<ISimulationContext>();
        var svc = new CombatService(cfg, reg, ctx);

        var ch = StubCharacter(CharacterClass.Warrior);
        ch.Health.Returns(400u);
        ch.IsDead.Returns(true);
        var pos = new Avalon.Common.Mathematics.Vector3(1, 2, 3);

        svc.RevivePlayer(ch, pos);

        Assert.False(ch.IsDead);
        ch.Received().CurrentHealth = 100u;       // 400 * 0.25
        ch.Received().Position      = pos;
        ctx.Received(1).BroadcastUnitRevive(ch, pos, 100u);
    }

    [Fact]
    public void Should_revive_at_minimum_one_hp_when_fraction_truncates_to_zero()
    {
        // Defensive: if MaxHealth * ReviveHealthFraction truncates to 0 but max>0, revive at 1 HP
        // so the state is internally consistent (alive but at 0 HP would be a degenerate state).
        var cfg = new CombatConfig { ReviveHealthFraction = 0.001f };
        var reg = new EncounterRegistry(cfg);
        var ctx = Substitute.For<ISimulationContext>();
        var svc = new CombatService(cfg, reg, ctx);

        var ch = StubCharacter(CharacterClass.Warrior);
        ch.Health.Returns(100u);          // 100 * 0.001 = 0.1 → truncates to 0
        ch.IsDead.Returns(true);
        var pos = new Avalon.Common.Mathematics.Vector3(0, 0, 0);

        svc.RevivePlayer(ch, pos);

        Assert.False(ch.IsDead);
        ch.Received().CurrentHealth = 1u;
        ctx.Received(1).BroadcastUnitRevive(ch, pos, 1u);
    }

    [Fact]
    public void Should_noop_revive_when_target_is_not_a_character()
    {
        // RevivePlayer is character-only (creatures use respawner, not revive).
        var cfg = new CombatConfig();
        var reg = new EncounterRegistry(cfg);
        var ctx = Substitute.For<ISimulationContext>();
        var svc = new CombatService(cfg, reg, ctx);

        var creature = Substitute.For<ICreature>();

        svc.RevivePlayer(creature, default);

        ctx.DidNotReceive().BroadcastUnitRevive(
            Arg.Any<IUnit>(),
            Arg.Any<Avalon.Common.Mathematics.Vector3>(),
            Arg.Any<uint>());
    }

    [Fact]
    public void Should_drop_player_zeroing_threat_across_all_hostiles()
    {
        var (svc, reg) = BuildService(initialThreatSeed: 0);
        var attacker = StubCharacter(CharacterClass.Warrior);
        var hostile1 = StubCreature();
        var hostile2 = StubCreature();
        var ability  = StubAbility(1.0f);

        svc.ApplyDamage(attacker, hostile1, 10, ability);
        svc.ApplyDamage(attacker, hostile2, 10, ability);

        svc.DropPlayerFromEncounter(attacker);

        var enc = (Encounter)System.Linq.Enumerable.First(reg.Active);
        Assert.DoesNotContain(attacker, enc.Players);
        Assert.False(enc.GetThreatList(hostile1).ContainsKey(attacker));
        Assert.False(enc.GetThreatList(hostile2).ContainsKey(attacker));
    }

    [Fact]
    public void Should_noop_drop_when_player_not_in_any_encounter()
    {
        var (svc, reg) = BuildService();
        var attacker = StubCharacter(CharacterClass.Warrior);

        var ex = Record.Exception(() => svc.DropPlayerFromEncounter(attacker));
        Assert.Null(ex);
        Assert.Empty(reg.Active);
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

    [Fact]
    public void Should_not_damage_a_creature_that_is_invulnerable()
    {
        // Town NPCs are never killable. The guard sits at the very top of ApplyDamageCore — the
        // single chokepoint every damage source funnels through — so an invulnerable target costs
        // no encounter, no threat, no combat tag, and never reaches OnHit.
        var (svc, reg) = BuildService();
        var attacker = StubCharacter(CharacterClass.Warrior);
        var target   = StubCreature();
        target.Invulnerable.Returns(true);
        var ability  = StubAbility(1.0f);

        svc.ApplyDamage(attacker, target, 25, ability);

        target.DidNotReceive().OnHit(Arg.Any<IUnit>(), Arg.Any<uint>());
        Assert.Empty(reg.Active);
        attacker.DidNotReceive().MarkCombat();
    }

    [Fact]
    public void Should_not_damage_an_invulnerable_creature_through_the_abilityless_overload()
    {
        // Creature melee (CreatureCombatScript) uses the 3-arg overload. Both overloads share
        // ApplyDamageCore, and this pins that they do — a guard added to only one would leave
        // creature-on-NPC damage live.
        var (svc, reg) = BuildService();
        var attacker = StubCreature();
        var target   = StubCreature();
        target.Invulnerable.Returns(true);

        svc.ApplyDamage(attacker, target, 25);

        target.DidNotReceive().OnHit(Arg.Any<IUnit>(), Arg.Any<uint>());
        Assert.Empty(reg.Active);
    }

    // ── #546: what a hit led to is reported to the service's own instance ──

    /// <summary>
    /// The kill is reported once, after the hit made the creature dead and before the encounter hears
    /// of the death and the death is broadcast: the order the static kill event used to run in.
    /// </summary>
    [Fact]
    public void Report_a_kill_once_before_the_encounter_death_and_the_death_broadcast()
    {
        var (svc, reg, ctx, outcomes) = BuildServiceWithOutcomes();
        var attacker = StubCharacter(CharacterClass.Warrior);
        var target   = StubCreature();
        target.CurrentHealth.Returns(10u);
        target.When(t => t.OnHit(attacker, 10)).Do(_ => target.CurrentHealth.Returns(0u));

        bool? hostileAtKill = null;
        bool deathBroadcastAtKill = true;
        outcomes.When(o => o.CreatureKilled(target, attacker)).Do(_ =>
        {
            hostileAtKill = reg.FindEncounterContaining(target)?.Hostiles.Contains(target);
            deathBroadcastAtKill = ctx.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(ISimulationContext.BroadcastUnitDeath));
        });

        svc.ApplyDamage(attacker, target, 10);

        outcomes.Received(1).CreatureKilled(target, attacker);
        Assert.True(hostileAtKill, "the encounter had already been told of the death when the kill was reported");
        Assert.False(deathBroadcastAtKill, "the death was broadcast before the kill was reported");
        ctx.Received(1).BroadcastUnitDeath(target, attacker);
        Assert.Null(reg.FindEncounterContaining(target)?.Hostiles.FirstOrDefault(h => ReferenceEquals(h, target)));
    }

    /// <summary>A corpse hit again is no second kill: only a creature brought from above 0 to 0 is.</summary>
    [Fact]
    public void Not_report_a_kill_for_a_creature_already_at_zero_health()
    {
        var (svc, _, _, outcomes) = BuildServiceWithOutcomes();
        var target = StubCreature();
        target.CurrentHealth.Returns(0u);

        svc.ApplyDamage(StubCharacter(CharacterClass.Warrior), target, 10);

        outcomes.DidNotReceiveWithAnyArgs().CreatureKilled(default!, default!);
    }

    /// <summary>A hit its script does not turn into a death (Returning ignores hits) kills nothing.</summary>
    [Fact]
    public void Not_report_a_kill_when_the_hit_leaves_the_creature_alive()
    {
        var (svc, _, _, outcomes) = BuildServiceWithOutcomes();
        var target = StubCreature();

        svc.ApplyDamage(StubCharacter(CharacterClass.Warrior), target, 10);

        outcomes.DidNotReceiveWithAnyArgs().CreatureKilled(default!, default!);
    }

    /// <summary>
    /// A living character's hit is reported with the ability that dealt it; a dead one's is not, as its
    /// OnHit ignores the hit and nothing was sent before either.
    /// </summary>
    [Fact]
    public void Report_a_living_characters_damage_and_nothing_for_a_corpse()
    {
        var (svc, _, _, outcomes) = BuildServiceWithOutcomes();
        var attacker = StubCreature();
        var target   = Avalon.Server.World.UnitTests.Inventory.TestCharacters.New(546);
        target.Health = 100;
        target.CurrentHealth = 100;
        var ability  = StubAbility(1.0f);
        ability.AbilityId.Returns(new Avalon.Common.ValueObjects.AbilityId(7));

        svc.ApplyDamage(attacker, target, 30, ability);

        outcomes.Received(1).CharacterDamaged(target, attacker, 30, new Avalon.Common.ValueObjects.AbilityId(7),
            Avalon.Network.Packets.Combat.HitResult.None);

        target.IsDead = true;
        outcomes.ClearReceivedCalls();
        svc.ApplyDamage(attacker, target, 30);

        outcomes.DidNotReceiveWithAnyArgs().CharacterDamaged(default!, default!, default, default, default);
    }

    // ── #588: a hit larger than a creature's remaining health kills it; health never wraps ──

    [Fact]
    public void Kill_a_creature_once_when_a_hit_exceeds_its_remaining_health()
    {
        var (svc, _, ctx, outcomes) = BuildServiceWithOutcomes();
        var (creature, _) = CreatureWithCombatScript(ctx, health: 30);
        var attacker = StubCharacter(CharacterClass.Warrior);

        svc.ApplyDamage(attacker, creature, 100);

        Assert.Equal(0u, creature.CurrentHealth);
        outcomes.Received(1).CreatureKilled(creature, attacker);
        ctx.Received(1).BroadcastUnitDeath(creature, attacker);
    }

    [Fact]
    public void Kill_a_creature_when_a_hit_equals_its_remaining_health()
    {
        var (svc, _, ctx, outcomes) = BuildServiceWithOutcomes();
        var (creature, _) = CreatureWithCombatScript(ctx, health: 30);
        var attacker = StubCharacter(CharacterClass.Warrior);

        svc.ApplyDamage(attacker, creature, 30);

        Assert.Equal(0u, creature.CurrentHealth);
        outcomes.Received(1).CreatureKilled(creature, attacker);
        ctx.Received(1).BroadcastUnitDeath(creature, attacker);
    }

    [Fact]
    public void Leave_the_rest_of_a_creatures_health_after_a_smaller_hit()
    {
        var (svc, _, ctx, outcomes) = BuildServiceWithOutcomes();
        var (creature, _) = CreatureWithCombatScript(ctx, health: 30);
        var attacker = StubCharacter(CharacterClass.Warrior);

        svc.ApplyDamage(attacker, creature, 12);

        Assert.Equal(18u, creature.CurrentHealth);
        ctx.Received(1).BroadcastUnitHit(attacker, creature, 18u, 12u);
        outcomes.DidNotReceiveWithAnyArgs().CreatureKilled(default!, default!);
        ctx.DidNotReceiveWithAnyArgs().BroadcastUnitDeath(default!, default!);
    }

    /// <summary>A creature walking home ignores hits, however large: it neither loses health nor dies.</summary>
    [Fact]
    public void Ignore_an_overkill_hit_on_a_returning_creature()
    {
        var (svc, _, ctx, outcomes) = BuildServiceWithOutcomes();
        var (creature, script) = CreatureWithCombatScript(ctx, health: 30);
        script.State = CreatureCombatScript.CombatState.Returning;

        svc.ApplyDamage(StubCharacter(CharacterClass.Warrior), creature, 100);

        Assert.Equal(30u, creature.CurrentHealth);
        outcomes.DidNotReceiveWithAnyArgs().CreatureKilled(default!, default!);
    }

    /// <summary>
    /// The service hands a creature's script at most the health it has left, so a script that
    /// subtracts the damage it is given cannot wrap health past 0 (ICreature.CurrentHealth is on the
    /// modding API, and any script may write it).
    /// </summary>
    [Fact]
    public void Hand_a_creatures_script_no_more_damage_than_its_remaining_health()
    {
        var (svc, _, _, _) = BuildServiceWithOutcomes();
        var attacker = StubCharacter(CharacterClass.Warrior);
        var target   = StubCreature();
        target.CurrentHealth.Returns(30u);

        svc.ApplyDamage(attacker, target, 100);

        target.Received(1).OnHit(attacker, 30u);
    }

    /// <summary>
    /// A corpse takes no hits: the second hit on a creature it killed sends no second death, puts the
    /// corpse back in no encounter (so the encounter hears of no second death) and tags nobody.
    /// </summary>
    [Fact]
    public void Refuse_a_hit_on_a_creature_that_is_already_dead()
    {
        var (svc, reg, ctx, outcomes) = BuildServiceWithOutcomes();
        var (creature, _) = CreatureWithCombatScript(ctx, health: 30);
        var killer = StubCharacter(CharacterClass.Warrior);
        svc.ApplyDamage(killer, creature, 100);
        ctx.ClearReceivedCalls();
        outcomes.ClearReceivedCalls();

        var other = StubCharacter(CharacterClass.Hunter);
        svc.ApplyDamage(other, creature, 50, StubAbility(1.0f));

        Assert.Equal(0u, creature.CurrentHealth);
        ctx.DidNotReceiveWithAnyArgs().BroadcastUnitDeath(default!, default!);
        ctx.DidNotReceiveWithAnyArgs().BroadcastUnitHit(default!, default!, default, default);
        outcomes.DidNotReceiveWithAnyArgs().CreatureKilled(default!, default!);
        Assert.Null(reg.FindEncounterContaining(creature));
        Assert.Null(reg.FindEncounterContaining(other));
        other.DidNotReceive().MarkCombat();
    }

    [Fact]
    public void Not_pass_a_hit_on_a_dead_creature_to_its_script()
    {
        var (svc, reg, ctx) = BuildServiceWithContext();
        var target = StubCreature();
        target.CurrentHealth.Returns(0u);

        svc.ApplyDamage(StubCharacter(CharacterClass.Warrior), target, 10);

        target.DidNotReceiveWithAnyArgs().OnHit(default!, default);
        ctx.DidNotReceiveWithAnyArgs().BroadcastUnitDeath(default!, default!);
        Assert.Empty(reg.Active);
    }

    /// <summary>
    /// The script clamps on its own too, for a hit that does not come through the service, and the killing
    /// blow is sent like any other hit (#506 review), for the health it took, at 0 left.
    /// </summary>
    [Fact]
    public void Kill_a_creature_whose_script_is_hit_directly_for_more_than_its_health()
    {
        var ctx = Substitute.For<ISimulationContext>();
        var (creature, script) = CreatureWithCombatScript(ctx, health: 30);
        var killer = StubCharacter(CharacterClass.Warrior);

        script.OnHit(killer, 100);

        Assert.Equal(0u, creature.CurrentHealth);
        ctx.Received(1).BroadcastUnitHit(killer, creature, 0u, 30u);
    }

    /// <summary>#506 review: the killing hit is broadcast before the death, as a hit that leaves health is.</summary>
    [Fact]
    public void Broadcast_the_killing_hit_before_the_death()
    {
        var (svc, _, ctx, _) = BuildServiceWithOutcomes();
        var (creature, _) = CreatureWithCombatScript(ctx, health: 30);
        var killer = StubCharacter(CharacterClass.Warrior);

        svc.ApplyDamage(killer, creature, 100);

        Received.InOrder(() =>
        {
            ctx.BroadcastUnitHit(killer, creature, 0u, 30u);
            ctx.BroadcastUnitDeath(creature, killer);
        });
    }

    // ── #610: a creature walking home ignores hits entirely ──

    public static TheoryData<string> ReturningScripts() =>
        new() { nameof(CreatureCombatScript), nameof(AggroDefendScript), nameof(CreaturePatrolScript) };

    /// <summary>
    /// A hit on a creature that is returning home is refused beside the invulnerable and corpse checks,
    /// whether the combat script is the creature's own or chained inside another: no encounter, no
    /// threat, no script call, no broadcast, no combat tag and no lost health.
    /// </summary>
    [Theory]
    [MemberData(nameof(ReturningScripts))]
    public void Refuse_a_hit_on_a_creature_that_is_returning_home(string scriptName)
    {
        var (svc, reg, ctx, outcomes) = BuildServiceWithOutcomes();
        Creature creature = CreatureReturningHome(ctx, scriptName, health: 30);
        var attacker = StubCharacter(CharacterClass.Warrior);

        svc.ApplyDamage(attacker, creature, 10, StubAbility(1.0f));

        Assert.Equal(30u, creature.CurrentHealth);
        Assert.Empty(reg.Active);
        Assert.Null(reg.FindEncounterContaining(attacker));
        attacker.DidNotReceive().MarkCombat();
        ctx.DidNotReceiveWithAnyArgs().BroadcastUnitHit(default!, default!, default, default);
        outcomes.DidNotReceiveWithAnyArgs().CreatureKilled(default!, default!);
    }

    /// <summary>
    /// A hit taken on the way home leaves nothing behind: once the creature is home and reset, the next
    /// fight is against whoever pulls it, not the unit that hit it while it walked back.
    /// </summary>
    [Fact]
    public void Leave_no_threat_from_a_hit_on_the_way_home_for_the_next_fight()
    {
        var cfg = new CombatConfig();
        var reg = new EncounterRegistry(cfg);
        var ctx = Substitute.For<ISimulationContext>();
        var svc = new CombatService(cfg, reg, ctx);
        ctx.CombatService.Returns(svc);
        var slots = Substitute.For<IMeleeSlots>();
        ctx.MeleeSlots.Returns(slots);
        var home = new Avalon.Common.Mathematics.Vector3(5f, 0f, 5f);
        var creature = new Creature
        {
            Guid          = new Avalon.Common.ObjectGuid(Avalon.Common.ObjectType.Creature, 610),
            Metadata      = Substitute.For<ICreatureMetadata>(),
            Name          = "Wolf",
            Health        = 30,
            CurrentHealth = 30,
            Position      = home,
        };
        var script = new CreatureCombatScript(NullLoggerFactory.Instance, creature, ctx);
        creature.Script = script;

        ICharacter first = StubCharacter(CharacterClass.Hunter);
        script.OnEnteredRange(first);
        script.OnCharacterLeft(first); // Returning; home is where it was pulled

        ICharacter hitOnTheWayHome = StubCharacter(CharacterClass.Warrior);
        hitOnTheWayHome.Guid.Returns(new Avalon.Common.ObjectGuid(Avalon.Common.ObjectType.Character, 1));
        hitOnTheWayHome.Position.Returns(home);
        svc.ApplyDamage(hitOnTheWayHome, creature, 20);

        script.Update(TimeSpan.FromSeconds(0.1)); // home: reset to idle
        Assert.Equal((object)CreatureCombatScript.CombatState.None, script.State);

        ICharacter puller = StubCharacter(CharacterClass.Hunter);
        puller.Guid.Returns(new Avalon.Common.ObjectGuid(Avalon.Common.ObjectType.Character, 2));
        puller.Position.Returns(home);
        script.OnEnteredRange(puller);
        script.Update(TimeSpan.FromSeconds(0.1));

        slots.ReceivedWithAnyArgs().TryClaim(default!, default!, default!, default!, out _);
        foreach (var call in slots.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IMeleeSlots.TryClaim)))
            Assert.Equal(puller.Guid, call.GetArguments()[0]);
    }

    /// <summary>
    /// Heal threat skips a creature walking home (#610), which would otherwise carry it past its reset
    /// and could turn the next pull on the healer. The creatures still fighting share the whole of it.
    /// </summary>
    [Fact]
    public void Add_no_heal_threat_to_a_creature_that_is_returning_home()
    {
        var (svc, reg) = BuildService(initialThreatSeed: 0);
        var healer = StubCharacter(CharacterClass.Healer);
        var ally   = StubCharacter(CharacterClass.Warrior);
        ally.Health.Returns(300u);
        Creature returning = CreatureReturningHome(Substitute.For<ISimulationContext>(), nameof(CreatureCombatScript), health: 30);
        var fighting = StubCreature();
        var heal = Substitute.For<IAbility>();
        heal.Metadata.Returns(new AbilityMetadata { Name = "H", ScriptName = "h", HealThreatPerHp = 0.5f });
        svc.EnterCombat(returning, ally);
        svc.EnterCombat(fighting, ally);

        svc.ApplyHeal(healer, ally, 100, heal); // 100 * 0.5 * 1.0 = 50

        var enc = (Encounter)reg.FindEncounterContaining(ally)!;
        enc.GetThreatList(returning).TryGetValue(healer, out float onReturning);
        Assert.Equal(0f, onReturning);
        Assert.Equal(50.0f, enc.GetThreatList(fighting)[healer], 3);
    }

    // ── #614: the combat clock, and nothing carried over the walk home ──

    [Fact]
    public void Time_a_taunt_by_the_services_clock()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2001, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var cfg = new CombatConfig();
        var reg = new EncounterRegistry(cfg, clock);
        var svc = new CombatService(cfg, reg, Substitute.For<ISimulationContext>(), time: clock);
        var tank = StubCharacter(CharacterClass.Warrior);
        var creature = StubCreature();
        svc.EnterCombat(creature, tank);

        svc.ApplyTaunt(tank, creature, 3000);

        Assert.Same(tank, creature.TauntedBy);
        Assert.Equal(clock.Now.UtcDateTime.AddMilliseconds(3000), creature.TauntExpiresAt);
    }

    /// <summary>
    /// Threat decays by the tick's delta; the time-based part of an encounter, its end once no hostile
    /// is left and the grace has passed since the last damage, follows the registry's clock.
    /// </summary>
    [Fact]
    public void End_an_encounter_by_its_clock_once_the_grace_has_passed()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2001, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var cfg = new CombatConfig { EncounterEndGraceSeconds = 5f };
        var reg = new EncounterRegistry(cfg, clock);
        var svc = new CombatService(cfg, reg, Substitute.For<ISimulationContext>(), time: clock);
        var creature = StubCreature();
        var player = StubCharacter(CharacterClass.Warrior);
        svc.ApplyDamage(player, creature, 10);
        ((Encounter)reg.Active.Single()).OnParticipantDied(creature);

        clock.Now = clock.Now.AddSeconds(4);
        svc.Update(TimeSpan.Zero);
        Assert.Single(reg.Active);

        clock.Now = clock.Now.AddSeconds(1);
        svc.Update(TimeSpan.Zero);
        Assert.Empty(reg.Active);
    }

    [Fact]
    public void Not_pull_a_creature_that_is_returning_home_into_combat()
    {
        var (svc, reg, ctx) = BuildServiceWithContext();
        Creature creature = CreatureReturningHome(ctx, nameof(CreatureCombatScript), health: 30);

        svc.EnterCombat(creature, StubCharacter(CharacterClass.Warrior));

        Assert.Empty(reg.Active);
    }

    [Fact]
    public void Not_taunt_a_creature_that_is_returning_home()
    {
        var (svc, reg, ctx) = BuildServiceWithContext();
        var (creature, script) = CreatureWithCombatScript(ctx, health: 30);
        var puller = StubCharacter(CharacterClass.Hunter);
        script.OnEnteredRange(puller);
        svc.EnterCombat(creature, puller);
        script.OnCharacterLeft(puller); // Returning, still in the encounter it fought in

        var tank = StubCharacter(CharacterClass.Warrior);
        svc.ApplyTaunt(tank, creature, 3000);

        Assert.Null(creature.TauntedBy);
        var enc = (Encounter)reg.Active.Single();
        Assert.DoesNotContain(tank, enc.Players);
        Assert.False(enc.GetThreatList(creature).ContainsKey(tank));
    }

    /// <summary>
    /// Home and reset, the creature leaves its encounter: nothing from before the leash, not even the
    /// seeded threat of a player it swung at, steers its next fight. The player it fought and the other
    /// creature they still fight stay in that encounter, their threat untouched.
    /// </summary>
    [Fact]
    public void Leave_its_encounter_once_home_so_its_next_fight_starts_clean()
    {
        var cfg = new CombatConfig();
        var reg = new EncounterRegistry(cfg);
        var ctx = Substitute.For<ISimulationContext>();
        var svc = new CombatService(cfg, reg, ctx);
        ctx.CombatService.Returns(svc);
        var slots = Substitute.For<IMeleeSlots>();
        ctx.MeleeSlots.Returns(slots);
        var home = new Avalon.Common.Mathematics.Vector3(5f, 0f, 5f);
        var creature = new Creature
        {
            Guid          = new Avalon.Common.ObjectGuid(Avalon.Common.ObjectType.Creature, 614),
            Metadata      = Substitute.For<ICreatureMetadata>(),
            Name          = "Wolf",
            Health        = 30,
            CurrentHealth = 30,
            Position      = home,
        };
        var script = new CreatureCombatScript(NullLoggerFactory.Instance, creature, ctx);
        creature.Script = script;

        ICharacter fought = StubCharacter(CharacterClass.Warrior);
        fought.Guid.Returns(new Avalon.Common.ObjectGuid(Avalon.Common.ObjectType.Character, 1));
        fought.Position.Returns(home);
        script.OnEnteredRange(fought);
        svc.ApplyDamage(creature, fought, 5);       // it swung at the player: seeded threat
        var other = StubCreature();
        svc.ApplyDamage(fought, other, 10);         // the player also fights another creature
        var enc = (Encounter)reg.FindEncounterContaining(fought)!;
        float threatOnOther = enc.GetThreatList(other)[fought];

        creature.Position = home + new Avalon.Common.Mathematics.Vector3(50f, 0f, 0f);
        script.Update(TimeSpan.FromSeconds(0.1));   // past the leash: Returning
        creature.Position = home;
        script.Update(TimeSpan.FromSeconds(0.1));   // home: reset to idle
        Assert.Equal((object)CreatureCombatScript.CombatState.None, script.State);

        Assert.Null(reg.FindEncounterContaining(creature));
        Assert.DoesNotContain(creature, enc.Hostiles);
        Assert.Contains(other, enc.Hostiles);
        Assert.Contains(fought, enc.Players);
        Assert.Equal(threatOnOther, enc.GetThreatList(other)[fought]);
        Assert.Same(enc, reg.FindEncounterContaining(fought));

        ICharacter puller = StubCharacter(CharacterClass.Hunter);
        puller.Guid.Returns(new Avalon.Common.ObjectGuid(Avalon.Common.ObjectType.Character, 2));
        puller.Position.Returns(home);
        slots.ClearReceivedCalls();
        script.OnEnteredRange(puller);
        script.Update(TimeSpan.FromSeconds(0.1));

        slots.ReceivedWithAnyArgs().TryClaim(default!, default!, default!, default!, out _);
        foreach (var call in slots.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IMeleeSlots.TryClaim)))
            Assert.Equal(puller.Guid, call.GetArguments()[0]);
    }

    /// <summary>
    /// ICombatService is the modding API, reachable by every AI script: a way to take a creature out of
    /// its encounter there would let a mod keep any creature from ever holding threat (#614).
    /// </summary>
    [Fact]
    public void Offer_no_way_to_drop_a_hostile_from_its_encounter_on_the_modding_api()
    {
        Assert.DoesNotContain(typeof(ICombatService).GetMembers(),
            m => m.Name.Contains("Hostile", StringComparison.Ordinal));
    }

    /// <summary>
    /// A creature that snaps home mid-taunt forgets the taunt at the reset, so the old taunter does not
    /// hold its next fight (#614).
    /// </summary>
    [Fact]
    public void Forget_a_taunt_once_home_so_its_next_fight_goes_to_whoever_pulls_it()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2001, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var cfg = new CombatConfig();
        var reg = new EncounterRegistry(cfg, clock);
        var ctx = Substitute.For<ISimulationContext>();
        var svc = new CombatService(cfg, reg, ctx, time: clock);
        ctx.CombatService.Returns(svc);
        var slots = Substitute.For<IMeleeSlots>();
        ctx.MeleeSlots.Returns(slots);
        var home = new Avalon.Common.Mathematics.Vector3(5f, 0f, 5f);
        var creature = new Creature
        {
            Guid          = new Avalon.Common.ObjectGuid(Avalon.Common.ObjectType.Creature, 614),
            Metadata      = Substitute.For<ICreatureMetadata>(),
            Name          = "Wolf",
            Health        = 30,
            CurrentHealth = 30,
            Position      = home,
        };
        var script = new CreatureCombatScript(NullLoggerFactory.Instance, creature, ctx, clock);
        creature.Script = script;

        ICharacter tank = StubCharacter(CharacterClass.Warrior);
        tank.Guid.Returns(new Avalon.Common.ObjectGuid(Avalon.Common.ObjectType.Character, 1));
        tank.Position.Returns(home);
        script.OnEnteredRange(tank);
        svc.EnterCombat(creature, tank);
        svc.ApplyTaunt(tank, creature, 60_000);
        script.OnCharacterLeft(tank);             // Returning, the taunt still running by the clock
        script.Update(TimeSpan.FromSeconds(0.1)); // home: reset to idle

        Assert.Null(creature.TauntedBy);
        Assert.Equal(DateTime.MinValue, creature.TauntExpiresAt);

        ICharacter puller = StubCharacter(CharacterClass.Hunter);
        puller.Guid.Returns(new Avalon.Common.ObjectGuid(Avalon.Common.ObjectType.Character, 2));
        puller.Position.Returns(home);
        slots.ClearReceivedCalls();
        script.OnEnteredRange(puller);
        script.Update(TimeSpan.FromSeconds(0.1));

        slots.ReceivedWithAnyArgs().TryClaim(default!, default!, default!, default!, out _);
        foreach (var call in slots.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IMeleeSlots.TryClaim)))
            Assert.Equal(puller.Guid, call.GetArguments()[0]);
    }

    /// <summary>
    /// Puts a creature's combat script into Returning through the scripts' own public calls, with the
    /// combat script either the creature's script or chained inside <paramref name="scriptName" />.
    /// </summary>
    internal static Creature CreatureReturningHome(ISimulationContext ctx, string scriptName, uint health)
    {
        ctx.MeleeSlots.Returns(Substitute.For<IMeleeSlots>());
        var creature = new Creature
        {
            Guid          = new Avalon.Common.ObjectGuid(Avalon.Common.ObjectType.Creature, 610),
            Metadata      = Substitute.For<ICreatureMetadata>(),
            Name          = "Wolf",
            Health        = health,
            CurrentHealth = health,
        };
        AiScript script = scriptName switch
        {
            nameof(CreatureCombatScript) => new CreatureCombatScript(NullLoggerFactory.Instance, creature, ctx),
            nameof(AggroDefendScript)    => new AggroDefendScript(NullLoggerFactory.Instance, creature, ctx),
            nameof(CreaturePatrolScript) => new CreaturePatrolScript(NullLoggerFactory.Instance, creature, ctx),
            _ => throw new ArgumentOutOfRangeException(nameof(scriptName)),
        };
        creature.Script = script;

        ICharacter puller = StubCharacter(CharacterClass.Hunter);
        script.OnEnteredRange(puller);
        script.OnCharacterLeft(puller);
        return creature;
    }

    private static (Creature, CreatureCombatScript) CreatureWithCombatScript(ISimulationContext ctx, uint health)
    {
        ctx.MeleeSlots.Returns(Substitute.For<IMeleeSlots>());
        var creature = new Creature
        {
            Guid          = new Avalon.Common.ObjectGuid(Avalon.Common.ObjectType.Creature, 588),
            Metadata      = Substitute.For<ICreatureMetadata>(),
            Name          = "Wolf",
            Health        = health,
            CurrentHealth = health,
        };
        var script = new CreatureCombatScript(NullLoggerFactory.Instance, creature, ctx);
        creature.Script = script;
        return (creature, script);
    }

    private static (CombatService, EncounterRegistry, ISimulationContext, ICombatOutcomes) BuildServiceWithOutcomes()
    {
        var cfg = new CombatConfig();
        var reg = new EncounterRegistry(cfg);
        var ctx = Substitute.For<ISimulationContext>();
        var outcomes = Substitute.For<ICombatOutcomes>();
        return (new CombatService(cfg, reg, ctx, outcomes: outcomes), reg, ctx, outcomes);
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
