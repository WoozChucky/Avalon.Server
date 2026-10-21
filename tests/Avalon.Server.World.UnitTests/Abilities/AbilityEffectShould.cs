using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Auras;
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

    /// <summary>A protected arrival (#1088) is passed over by hostile shapes, never by helpful ones.</summary>
    [Theory]
    [InlineData(SpellEffect.Damage, false)]
    [InlineData(SpellEffect.Debuff, false)]
    [InlineData(SpellEffect.Heal, true)]
    public void Pass_over_a_protected_arrival_only_for_hostile_effects(SpellEffect effects, bool affected)
    {
        var arena = new TestArena();
        bool hostile = effects != SpellEffect.Heal;
        CharacterEntity caster = arena.Player(1, 0f, 0f, pvp: hostile);
        CharacterEntity arrival = arena.Player(2, 0f, 1f, pvp: hostile);
        AbilityTemplate row = AbilityTestData.Circle(234);
        row.Effects = effects;
        IAbility ability = hostile ? AbilityTestData.Game(row) : Blessing(effects);
        ArrivalProtectionTestData.Apply(arrival);

        Assert.Equal(affected ? 1 : 0, AbilityEffect.ApplyToAll(arena, caster, ability, [arrival]));
        Assert.Equal(affected ? 1 : 0, arena.AurasApplied.Count);

        ArrivalProtection.End(arrival);
        Assert.Equal(1, AbilityEffect.ApplyToAll(arena, caster, ability, [arrival]));
    }
}
