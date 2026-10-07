using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Abilities.Targeting;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;

namespace Avalon.Server.World.UnitTests.Abilities;

/// <summary>What an ability does to one unit its shape affects: the direct amount its Effects carries, then its aura.</summary>
public class AbilityEffectShould
{
    private static IAbility Strike(SpellEffect effects = SpellEffect.Damage)
    {
        AbilityTemplate row = AbilityTestData.Cone(203);
        row.Effects = effects;
        row.AuraId = new AuraId(901);
        return AbilityTestData.Game(row);
    }

    private static IAbility Blessing(SpellEffect effects)
    {
        AbilityTemplate row = AbilityTestData.HealCircle(233);
        row.Effects = effects;
        row.AuraId = new AuraId(904);
        return AbilityTestData.Game(row);
    }

    [Theory]
    [InlineData(HitOutcome.Ignored)]
    [InlineData(HitOutcome.Dodged)]
    public void Apply_no_aura_when_the_hit_did_not_land(HitOutcome outcome)
    {
        var arena = new TestArena { NextOutcome = outcome };
        CharacterEntity caster = arena.Player(1, 0f, 0f);
        ICreature boar = arena.Creature(0f, 2f, invulnerable: outcome == HitOutcome.Ignored);

        AbilityEffect.Apply(arena, caster, Strike(), boar);

        Assert.Equal([boar], arena.Damaged());
        Assert.Empty(arena.AurasApplied);
    }

    [Fact]
    public void Apply_the_aura_after_a_hit_that_landed()
    {
        var arena = new TestArena();
        CharacterEntity caster = arena.Player(1, 0f, 0f);
        ICreature boar = arena.Creature(0f, 2f);
        IAbility strike = Strike();

        AbilityEffect.Apply(arena, caster, strike, boar);

        Assert.Equal([boar], arena.Damaged());
        (IUnit? who, IUnit? target, IAbility? ability) = Assert.Single(arena.AurasApplied);
        Assert.Same(caster, who);
        Assert.Same(boar, target);
        Assert.Same(strike, ability);
    }

    [Fact]
    public void Apply_a_harmful_aura_only_ability_without_hitting()
    {
        var arena = new TestArena { NextOutcome = HitOutcome.Dodged };
        CharacterEntity caster = arena.Player(1, 0f, 0f);
        ICreature boar = arena.Creature(0f, 2f);

        AbilityEffect.Apply(arena, caster, Strike(SpellEffect.Debuff), boar);

        Assert.Empty(arena.Damaged());
        Assert.Single(arena.AurasApplied);
    }

    [Fact]
    public void Skip_the_heal_of_an_ally_ability_without_the_heal_flag_and_still_apply_its_aura()
    {
        var arena = new TestArena();
        CharacterEntity caster = arena.Player(1, 0f, 0f);
        CharacterEntity ally = arena.Player(2, 0f, 2f);
        IAbility blessing = Blessing(SpellEffect.Buff);

        AbilityEffect.Apply(arena, caster, blessing, ally);

        Assert.Empty(arena.Healed());
        (IUnit? who, IUnit? target, IAbility? ability) = Assert.Single(arena.AurasApplied);
        Assert.Same(caster, who);
        Assert.Same(ally, target);
        Assert.Same(blessing, ability);
    }

    [Fact]
    public void Heal_then_apply_the_aura_of_an_ally_ability_with_the_heal_flag()
    {
        var arena = new TestArena();
        CharacterEntity caster = arena.Player(1, 0f, 0f);
        CharacterEntity ally = arena.Player(2, 0f, 2f);

        AbilityEffect.Apply(arena, caster, Blessing(SpellEffect.Heal | SpellEffect.Buff), ally);

        Assert.Equal([ally], arena.Healed());
        Assert.Single(arena.AurasApplied);
    }
}
