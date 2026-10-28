using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Server.World.UnitTests.Auras;
using Avalon.World.Abilities.Targeting;
using Avalon.World.Auras;
using Avalon.World.Entities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;
using Avalon.World.Testing;

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

        Assert.Equal(affected ? 1 : 0, AbilityEffect.ApplyToAll(arena, caster, ability, [arrival], maxTargets: 0));
        Assert.Equal(affected ? 1 : 0, arena.AurasApplied.Count);

        ArrivalProtection.End(arrival);
        Assert.Equal(1, AbilityEffect.ApplyToAll(arena, caster, ability, [arrival], maxTargets: 0));
    }

    /// <summary>
    /// #1181: the capped path allocates nothing on the hit, the unit every hit ignores in front included, and neither
    /// does a second hit (Part D: shaped <c>SecondHit</c> 50, each unit hit whole and then at 50 %). The counts first
    /// prove the window runs the capped break and both hits; the cap itself is pinned end to end in
    /// MapInstanceAbilityCastShould.
    /// </summary>
    [Fact]
    public void Affect_at_most_the_cap_without_allocating()
    {
        var arena = new TestArena();
        CharacterEntity caster = arena.Player(1, 0f, 0f);
        Creature homeward = Boar(5, 1f);
        homeward.Script = HomewardScript.Walking(homeward, caster);
        IUnit[] candidates = [arena.Player(2, 0f, 0.8f), homeward, Boar(3, 1.2f), Boar(4, 2f)];
        AbilityTemplate row = AbilityTestData.Cone(219);
        row.Effects = SpellEffect.Debuff;   // no direct amount: the arena records no damage on its substitute
        IAbility ability = AbilityTestData.Shaped(row, (TalentModifierKind.SecondHit, 50, null));
        var second = new HitShaping(50f, AbilityTags.None);

        Assert.Equal(1, AbilityEffect.ApplyToAll(arena, caster, ability, candidates, maxTargets: 1));
        Assert.Equal(2, AbilityEffect.ApplyToAll(arena, caster, ability, candidates, maxTargets: 0));
        Assert.Equal([HitShaping.Whole, second, HitShaping.Whole, second, HitShaping.Whole, second], arena.Shapings);
        Assert.Equal(3, arena.AurasApplied.Count);   // once per unit: only the first landed hit applies it

        long before = ThreadAllocations.BeforeQuietWindow();
        for (int i = 0; i < 1000; i++)
        {
            // The recordings keep their capacity, so clearing them leaves only the hits in the window.
            arena.Shapings.Clear();
            arena.AurasApplied.Clear();
            AbilityEffect.ApplyToAll(arena, caster, ability, candidates, maxTargets: 1);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static Creature Boar(uint id, float z) => new()
    {
        Guid = new ObjectGuid(ObjectType.Creature, id),
        Metadata = Loot.LootTestData.BoarTemplate(null),
        Position = new Vector3(0f, 0f, z),
        Health = 50,
        CurrentHealth = 50,
    };
}
