using Avalon.Common.ValueObjects;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using NSubstitute;
using Xunit;

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
}
