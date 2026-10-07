using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Abilities.Targeting;
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
}
