using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Combat;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Combat;

/// <summary>ApplyHeal restores health (#164). It used to add heal threat only.</summary>
public class CombatServiceHealShould
{
    private readonly CombatConfig _config = new();
    private readonly EncounterRegistry _registry;
    private readonly CombatService _combat;

    public CombatServiceHealShould()
    {
        _registry = new EncounterRegistry(_config);
        _combat = new CombatService(_config, _registry);
    }

    private static CharacterEntity Wounded(uint id, uint max, uint current)
    {
        CharacterEntity character = TestCharacters.New(id);
        character.Health = max;
        character.CurrentHealth = current;
        return character;
    }

    private static IAbility Heal(float threatPerHp = 0f)
    {
        var ability = Substitute.For<IAbility>();
        ability.AbilityId.Returns(new AbilityId(232));
        ability.Metadata.Returns(new AbilityMetadata { Name = "Heal", ScriptName = "x", HealThreatPerHp = threatPerHp });
        return ability;
    }

    [Fact]
    public void Restore_the_amount()
    {
        CharacterEntity target = Wounded(1, max: 100, current: 40);

        _combat.ApplyHeal(TestCharacters.New(2), target, 25, Heal());

        Assert.Equal(65u, target.CurrentHealth);
    }

    [Fact]
    public void Cap_at_the_maximum()
    {
        CharacterEntity target = Wounded(1, max: 100, current: 90);

        _combat.ApplyHeal(TestCharacters.New(2), target, 25, Heal());

        Assert.Equal(100u, target.CurrentHealth);
    }

    [Fact]
    public void Never_heal_a_dead_character()
    {
        CharacterEntity target = Wounded(1, max: 100, current: 0);
        target.IsDead = true;

        _combat.ApplyHeal(TestCharacters.New(2), target, 25, Heal());

        Assert.Equal(0u, target.CurrentHealth);
        Assert.True(target.IsDead);
    }

    [Fact]
    public void Never_heal_a_creature_at_0_health()
    {
        var corpse = Substitute.For<ICreature>();
        corpse.Health.Returns(100u);
        corpse.CurrentHealth.Returns(0u);

        _combat.ApplyHeal(TestCharacters.New(2), corpse, 25, Heal());

        corpse.DidNotReceive().CurrentHealth = Arg.Any<uint>();
    }

    [Fact]
    public void Keep_adding_heal_threat_to_the_hostiles_fighting_the_target()
    {
        CharacterEntity healer = TestCharacters.New(2);
        CharacterEntity target = Wounded(1, max: 100, current: 40);
        var wolf = Substitute.For<ICreature>();
        _combat.EnterCombat(wolf, target);

        _combat.ApplyHeal(healer, target, 20, Heal(threatPerHp: 0.5f));

        var encounter = (Encounter)_combat.GetEncounterFor(target)!;
        Assert.True(encounter.GetThreatList(wolf).TryGetValue(healer, out float threat));
        Assert.True(threat > 0f);
    }

    [Fact]
    public void Add_no_threat_and_no_healer_for_a_heal_on_a_target_at_full_health()
    {
        CharacterEntity healer = TestCharacters.New(2);
        CharacterEntity target = Wounded(1, max: 100, current: 100);
        var wolf = Substitute.For<ICreature>();
        _combat.EnterCombat(wolf, target);

        _combat.ApplyHeal(healer, target, 50, Heal(threatPerHp: 0.5f));

        var encounter = (Encounter)_combat.GetEncounterFor(target)!;
        Assert.DoesNotContain(healer, encounter.Players);
        Assert.False(encounter.GetThreatList(wolf).ContainsKey(healer));
        Assert.Null(_combat.GetEncounterFor(healer));
    }

    [Fact]
    public void Count_only_the_health_restored_by_a_partial_overheal()
    {
        CharacterEntity healer = TestCharacters.New(2);
        CharacterEntity target = Wounded(1, max: 100, current: 70);
        var wolf = Substitute.For<ICreature>();
        var boar = Substitute.For<ICreature>();
        _combat.EnterCombat(wolf, target);
        _combat.EnterCombat(boar, target);

        _combat.ApplyHeal(healer, target, 100, Heal(threatPerHp: 0.5f));

        var encounter = (Encounter)_combat.GetEncounterFor(target)!;
        Assert.Equal(2, encounter.Hostiles.Count);
        // Joining seeds the healer at InitialThreatSeed; the heal adds its share on top.
        float expected = _config.InitialThreatSeed + 30 * 0.5f * ClassThreatModifier.Get(healer.Class) / 2;
        Assert.Equal(expected, encounter.GetThreatList(wolf)[healer], 3);
        Assert.Equal(expected, encounter.GetThreatList(boar)[healer], 3);
    }

    [Fact]
    public void Count_the_whole_amount_when_the_heal_restores_all_of_it()
    {
        CharacterEntity healer = TestCharacters.New(2);
        CharacterEntity target = Wounded(1, max: 100, current: 40);
        var wolf = Substitute.For<ICreature>();
        var boar = Substitute.For<ICreature>();
        _combat.EnterCombat(wolf, target);
        _combat.EnterCombat(boar, target);

        _combat.ApplyHeal(healer, target, 20, Heal(threatPerHp: 0.5f));

        var encounter = (Encounter)_combat.GetEncounterFor(target)!;
        float expected = _config.InitialThreatSeed + 20 * 0.5f * ClassThreatModifier.Get(healer.Class) / 2;
        Assert.Equal(expected, encounter.GetThreatList(wolf)[healer], 3);
        Assert.Equal(expected, encounter.GetThreatList(boar)[healer], 3);
    }

    /// <summary>Item use: a fixed amount, capped, reported, adding no threat and joining no encounter.</summary>
    [Fact]
    public void Restore_an_items_heal_capped_and_report_what_it_restored()
    {
        var outcomes = Substitute.For<ICombatOutcomes>();
        var combat = new CombatService(_config, _registry, outcomes: outcomes);
        CharacterEntity user = Wounded(1, max: 100, current: 80);

        uint restored = combat.RestoreHealth(user, user, 30);

        Assert.Equal(20u, restored);
        Assert.Equal(100u, user.CurrentHealth);
        outcomes.Received(1).UnitHealed(user, user, 20u, null, HitResult.None);
    }

    /// <summary>
    /// An item's heal on a player in a fight gives its healer no threat and does not bring it into the encounter, where
    /// an ability's heal (above) would do both.
    /// </summary>
    [Fact]
    public void Restore_an_items_heal_without_threat_or_joining_the_encounter()
    {
        CharacterEntity healer = TestCharacters.New(2);
        CharacterEntity target = Wounded(1, max: 100, current: 40);
        var wolf = Substitute.For<ICreature>();
        _combat.EnterCombat(wolf, target);
        var encounter = (Encounter)_combat.GetEncounterFor(target)!;

        Assert.Equal(20u, _combat.RestoreHealth(healer, target, 20));

        Assert.Equal(60u, target.CurrentHealth);
        Assert.False(encounter.GetThreatList(wolf).ContainsKey(healer));
        Assert.Null(_combat.GetEncounterFor(healer));
        Assert.False(healer.IsInCombat);
    }

    [Fact]
    public void Restore_nothing_to_a_dead_character_or_one_at_full_health()
    {
        var outcomes = Substitute.For<ICombatOutcomes>();
        var combat = new CombatService(_config, _registry, outcomes: outcomes);
        CharacterEntity dead = Wounded(1, max: 100, current: 0);
        dead.IsDead = true;
        CharacterEntity full = Wounded(2, max: 100, current: 100);

        Assert.Equal(0u, combat.RestoreHealth(dead, dead, 30));
        Assert.Equal(0u, combat.RestoreHealth(full, full, 30));
        outcomes.DidNotReceiveWithAnyArgs().UnitHealed(default!, default!, default, default, default);
    }
}
