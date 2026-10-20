using Avalon.Server.World.UnitTests.Auras;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Abilities.Targeting;
using Avalon.World.Auras;
using Avalon.World.Entities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;

namespace Avalon.Server.World.UnitTests.Abilities;

/// <summary>What an ability does to one unit its shape affects: the direct amount its Effects carries, then its aura.</summary>
public class AbilityEffectShould
{
    private static IAbility Blessing(SpellEffect effects)
    {
        AbilityTemplate row = AbilityTestData.HealCircle(233);
        row.Effects = effects;
        row.AuraId = new AuraId(904);
        return AbilityTestData.Game(row);
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

    [Theory]
    [InlineData(SpellEffect.Damage)]
    [InlineData(SpellEffect.Debuff)]
    public void Skip_protected_hostile_targets_in_shapes_and_when_applying_a_hit(SpellEffect effects)
    {
        var arena = new TestArena();
        CharacterEntity caster = arena.Player(1, 0f, 0f, pvp: true);
        CharacterEntity arrival = arena.Player(2, 0f, 1f, pvp: true);
        AbilityTemplate row = AbilityTestData.Circle(234);
        row.Effects = effects;
        IAbility ability = AbilityTestData.Game(row);
        ArrivalTestData.Apply(arrival);

        Assert.Equal(0, AbilityEffect.ApplyToAll(arena, caster, ability, [arrival]));
        AbilityEffect.Apply(arena, caster, ability, arrival);

        Assert.Empty(arena.Damaged());
        Assert.Empty(arena.AurasApplied);
        Assert.False(AbilityEffect.Qualifies(arena, caster, Blessing(SpellEffect.Heal), arrival));

        ArrivalProtection.End(arrival);
        Assert.Equal(1, AbilityEffect.ApplyToAll(arena, caster, ability, [arrival]));
        Assert.Single(arena.AurasApplied);
    }

    [Fact]
    public void Still_heal_a_protected_ally()
    {
        var arena = new TestArena();
        CharacterEntity ally = arena.Player(1, 0f, 0f);
        CharacterEntity arrival = arena.Player(2, 0f, 1f);
        ArrivalTestData.Apply(arrival);

        Assert.Equal(1, AbilityEffect.ApplyToAll(arena, ally, Blessing(SpellEffect.Heal), [arrival]));
        Assert.Equal([arrival], arena.Healed());
        Assert.Single(arena.AurasApplied);
    }
}
