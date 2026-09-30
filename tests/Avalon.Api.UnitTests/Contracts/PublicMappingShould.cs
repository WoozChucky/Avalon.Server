using Avalon.Api.Contract;
using Avalon.Api.Contract.Mappers;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Xunit;
using DomainFlags = Avalon.Domain.World.ItemTemplateFlags;

namespace Avalon.Api.UnitTests.Contracts;

/// <summary>What the public tooltip endpoints show of a template, and nothing more.</summary>
public class PublicMappingShould
{
    private static ItemTemplate Helm() => new()
    {
        Id = new ItemTemplateId(12), Name = "Barkplate Helm", Rarity = Avalon.Domain.World.ItemRarity.Uncommon,
        Class = Avalon.Domain.World.ItemClass.Armor, SubClass = Avalon.Domain.World.ItemSubClass.Helmet,
        Slot = Avalon.Domain.World.ItemSlotType.Head, RequiredLevel = 3, ItemPower = 8, MaxStackSize = 1,
        SellPrice = 125, AllowedClasses = [CharacterClass.Warrior],
        StatType1 = Avalon.Domain.World.StatType.Strength, StatValue1 = 1,
        StatType3 = Avalon.Domain.World.StatType.Armor, StatValue3 = 4,
    };

    [Fact]
    public void Map_an_items_tooltip_fields()
    {
        PublicItemDto dto = Helm().ToPublicDto();

        Assert.Equal(12ul, dto.Id);
        Assert.Equal("Barkplate Helm", dto.Name);
        Assert.Equal(Avalon.Api.Contract.ItemRarity.Uncommon, dto.Rarity);
        Assert.Equal(Avalon.Api.Contract.ItemSlotType.Head, dto.Slot);
        Assert.Equal((ushort)3, dto.RequiredLevel);
        Assert.Equal((ushort)8, dto.ItemPower);
        Assert.Equal(125u, dto.SellPrice);
        Assert.False(dto.CannotBeSold);
        Assert.Equal([CharacterClass.Warrior], dto.AllowedClasses);
        Assert.Empty(dto.Damage);
    }

    [Fact]
    public void List_only_the_stat_pairs_that_are_set_in_slot_order()
    {
        PublicItemDto dto = Helm().ToPublicDto();

        Assert.Collection(dto.Stats,
            s => Assert.Equal((Avalon.Api.Contract.StatType.Strength, 1u), (s.Type, s.Value)),
            s => Assert.Equal((Avalon.Api.Contract.StatType.Armor, 4u), (s.Type, s.Value)));
    }

    [Fact]
    public void List_both_damage_pairs_when_set()
    {
        ItemTemplate sword = Helm();
        sword.DamageMin1 = 12; sword.DamageMax1 = 18; sword.DamageType1 = Avalon.Domain.World.DamageType.Physical;
        sword.DamageMin2 = 2; sword.DamageMax2 = 4; sword.DamageType2 = Avalon.Domain.World.DamageType.Fire;

        Assert.Collection(sword.ToPublicDto().Damage,
            d => Assert.Equal((12u, 18u, Avalon.Api.Contract.DamageType.Physical), (d.Min, d.Max, d.Type)),
            d => Assert.Equal((2u, 4u, Avalon.Api.Contract.DamageType.Fire), (d.Min, d.Max, d.Type)));
    }

    [Fact]
    public void Skip_a_damage_pair_without_a_maximum()
    {
        ItemTemplate odd = Helm();
        odd.DamageMin1 = 5; odd.DamageMax1 = 0; odd.DamageType1 = Avalon.Domain.World.DamageType.Physical;
        odd.DamageMin2 = 3; odd.DamageMax2 = null;

        Assert.Empty(odd.ToPublicDto().Damage);
    }

    [Theory]
    [InlineData(DomainFlags.None, ItemAttunement.None)]
    [InlineData(DomainFlags.AttuneOnPickup, ItemAttunement.OnPickup)]
    [InlineData(DomainFlags.AttuneOnEquip, ItemAttunement.OnEquip)]
    [InlineData(DomainFlags.AttuneOnUse, ItemAttunement.OnUse)]
    [InlineData(DomainFlags.AttuneOnAccount, ItemAttunement.ToAccount)]
    public void Name_the_attunement(DomainFlags flags, ItemAttunement expected)
    {
        ItemTemplate item = Helm();
        item.Flags = flags;
        Assert.Equal(expected, item.ToPublicDto().Attunes);
    }

    [Theory]
    [InlineData(DomainFlags.None, ItemUniqueness.None)]
    [InlineData(DomainFlags.Unique, ItemUniqueness.Unique)]
    [InlineData(DomainFlags.UniqueEquipped, ItemUniqueness.UniqueEquipped)]
    public void Name_the_uniqueness(DomainFlags flags, ItemUniqueness expected)
    {
        ItemTemplate item = Helm();
        item.Flags = flags;
        Assert.Equal(expected, item.ToPublicDto().Unique);
    }

    [Fact]
    public void Say_an_item_cannot_be_sold()
    {
        ItemTemplate item = Helm();
        item.Flags = DomainFlags.NoSell | DomainFlags.AttuneOnPickup;

        PublicItemDto dto = item.ToPublicDto();
        Assert.True(dto.CannotBeSold);
        Assert.Equal(ItemAttunement.OnPickup, dto.Attunes);
    }

    [Theory]
    [InlineData("ConeAbilityScript", AbilityAffects.Hostile, Avalon.Api.Contract.AbilityAmountKind.Damage)]
    [InlineData("CircleAbilityScript", AbilityAffects.Ally, Avalon.Api.Contract.AbilityAmountKind.Healing)]
    [InlineData("ChargeAbilityScript", AbilityAffects.Hostile, Avalon.Api.Contract.AbilityAmountKind.None)]
    public void Map_an_abilitys_tooltip_fields(string script, AbilityAffects affects,
        Avalon.Api.Contract.AbilityAmountKind kind)
    {
        AbilityTemplate cleave = new()
        {
            Id = new AbilityId(210), Name = "Cleave", Cost = 20,
            CostPowerType = Avalon.Network.Packets.State.PowerType.Fury, CastTime = 0, Cooldown = 800,
            ScriptName = script, Affects = affects, EffectValue = 10, ScalingStat = ScalingStat.Attack,
            ScalingCoefficient = 0.5f, BaseDamageCoefficient = 1f, AllowedClasses = [CharacterClass.Warrior],
        };

        PublicAbilityDto dto = cleave.ToPublicDto();

        Assert.Equal((210u, "Cleave", 20u, 800u), (dto.Id, dto.Name, dto.Cost, dto.Cooldown));
        Assert.Equal(Avalon.Api.Contract.PowerType.Fury, dto.CostPowerType);
        Assert.Equal(kind, dto.AmountKind);
        Assert.Equal((10u, 0.5f, 1f), (dto.EffectValue, dto.ScalingCoefficient, dto.BaseDamageCoefficient));
        Assert.Equal(Avalon.Api.Contract.AbilityScalingStat.Attack, dto.ScalingStat);
    }
}
