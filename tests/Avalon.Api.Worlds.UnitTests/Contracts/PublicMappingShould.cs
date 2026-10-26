using Avalon.Api.Contract;
using Avalon.Api.Contract.Mappers;
using Avalon.Api.Testing;
using Avalon.Api.Worlds.Services;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.World;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Auth;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using DomainFlags = Avalon.Domain.World.ItemTemplateFlags;

namespace Avalon.Api.Worlds.UnitTests.Contracts;

/// <summary>What the public tooltip endpoints show of a template, and nothing more.</summary>
public class PublicMappingShould
{
    private static ItemTemplate Helm() => new()
    {
        Id = new ItemTemplateId(12),
        Name = "Barkplate Helm",
        Rarity = Avalon.Common.ItemRarity.Common,
        Class = Avalon.Domain.World.ItemClass.Armor,
        SubClass = Avalon.Domain.World.ItemSubClass.Helmet,
        Slot = Avalon.Domain.World.ItemSlotType.Head,
        RequiredLevel = 3,
        MaxStackSize = 1,
        SellPrice = 125,
        AllowedClasses = [CharacterClass.Warrior],
        StatType1 = Avalon.Domain.World.StatType.Strength,
        StatValue1 = 1,
        StatType3 = Avalon.Domain.World.StatType.Armor,
        StatValue3 = 4,
    };

    [Fact]
    public void List_only_the_stat_pairs_that_are_set_in_slot_order()
    {
        PublicItemDto dto = Helm().ToPublicDto([]);

        Assert.Collection(dto.Stats,
            s => Assert.Equal((Avalon.Api.Contract.StatType.Strength, 1u), (s.Type, s.Value)),
            s => Assert.Equal((Avalon.Api.Contract.StatType.Armor, 4u), (s.Type, s.Value)));
    }

    /// <summary>
    /// #1179, read from the seeded world as the tooltip reads it: a base shows the ranges every copy rolls within, by
    /// stat, and its damage type; a unique names its base, carries no ranges of its own (its base, a ring, has none), and
    /// lists the implicit it inherits from its base before its own lines.
    /// </summary>
    [Fact]
    public async Task List_a_bases_stat_ranges_and_a_uniques_inherited_ones()
    {
        using var sqlite = new SqliteWorlds(1);
        var factory = new DelegateDbContextFactory<WorldDbContext>(() => sqlite.CreateWorld(new WorldId(1)));
        var items = new ItemTemplateRepository(factory);
        var modifiers = new ItemModifierRepository(factory, NullLoggerFactory.Instance);
        async Task<PublicItemDto> Read(ulong id) =>
            await PublicItems.ToPublicAsync((await items.FindByIdAsync(new ItemTemplateId(id), track: false))!, items,
                modifiers, CancellationToken.None);
        static string Spans(PublicItemDto item) => string.Join(", ", item.ModifierSpans.Select(s =>
            $"{s.Kind} {s.Stat} {s.Min}-{s.Max}" + (s.Min2 is null ? "" : $" {s.Min2}-{s.Max2}")));

        PublicItemDto sword = await Read(32);
        PublicItemDto unique = await Read(65);

        Assert.Equal((Avalon.Api.Contract.DamageType?)Avalon.Api.Contract.DamageType.Physical, sword.DamageType);
        Assert.Null(sword.BaseItemId);
        Assert.Equal(
            [(Avalon.Api.Contract.ItemBaseStat.DamageMin, 2, 4), (Avalon.Api.Contract.ItemBaseStat.DamageMax, 6, 8),
             (Avalon.Api.Contract.ItemBaseStat.AttackTime, 665, 735)],
            sword.BaseStats.Select(s => (s.Stat, s.Min, s.Max)));
        Assert.Equal((125ul, (Avalon.Api.Contract.DamageType?)null), (unique.BaseItemId, unique.DamageType));
        Assert.Empty(unique.BaseStats);
        Assert.Equal(
            "Implicit AddedLightningDamageToAttacks 1-1 2-2, Unique Health 4-5, " +
            "Unique AddedPhysicalDamageToAttacks 1-1 3-3, Unique CritMultiplier 3-4",
            Spans(unique));
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
        Assert.Equal(expected, item.ToPublicDto([]).Attunes);
    }

    [Theory]
    [InlineData(DomainFlags.None, ItemUniqueness.None)]
    [InlineData(DomainFlags.Unique, ItemUniqueness.Unique)]
    [InlineData(DomainFlags.UniqueEquipped, ItemUniqueness.UniqueEquipped)]
    public void Name_the_uniqueness(DomainFlags flags, ItemUniqueness expected)
    {
        ItemTemplate item = Helm();
        item.Flags = flags;
        Assert.Equal(expected, item.ToPublicDto([]).Unique);
    }

    [Fact]
    public void Say_an_item_cannot_be_sold()
    {
        ItemTemplate item = Helm();
        item.Flags = DomainFlags.NoSell | DomainFlags.AttuneOnPickup;

        PublicItemDto dto = item.ToPublicDto([]);
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
            Id = new AbilityId(210),
            Name = "Cleave",
            Cost = 20,
            CastTime = 1500,
            Cooldown = 800,
            Range = Avalon.World.Public.Enums.SpellRange.Melee,
            ScriptName = script,
            Affects = affects,
            AllowedClasses = [CharacterClass.Warrior],
            Effects = affects == AbilityAffects.Ally
                ? Avalon.World.Public.Enums.SpellEffect.Heal
                : Avalon.World.Public.Enums.SpellEffect.Damage,
        };

        PublicAbilityDto dto = cleave.ToPublicDto();

        Assert.Equal((210u, "Cleave", 20u, 800u), (dto.Id, dto.Name, dto.Cost, dto.Cooldown));
        Assert.Equal(kind, dto.AmountKind);
        Assert.Equal(Avalon.Api.Contract.SpellRange.Melee, dto.Range);
        Assert.Equal(1500u, dto.CastTime);
        Assert.Equal([CharacterClass.Warrior], dto.AllowedClasses);
    }

    /// <summary>
    /// The API casts these from the stored enums by ordinal (the public tooltips, the templates, the quest log), as the
    /// world casts an item modifier's stat to the wire's, so each member must keep its name across the pair.
    /// </summary>
    [Theory]
    [InlineData(typeof(Avalon.Common.ItemRarity), typeof(Avalon.Api.Contract.ItemRarity))]
    [InlineData(typeof(Avalon.Domain.World.ItemClass), typeof(Avalon.Api.Contract.ItemClass))]
    [InlineData(typeof(Avalon.Domain.World.ItemSubClass), typeof(Avalon.Api.Contract.ItemSubClass))]
    [InlineData(typeof(Avalon.Domain.World.ItemSlotType), typeof(Avalon.Api.Contract.ItemSlotType))]
    [InlineData(typeof(Avalon.Domain.World.ItemBaseKind), typeof(Avalon.Api.Contract.ItemBaseKind))]
    [InlineData(typeof(Avalon.Domain.World.ItemModifierKind), typeof(Avalon.Api.Contract.ItemModifierKind))]
    [InlineData(typeof(Avalon.Domain.World.StatType), typeof(Avalon.Api.Contract.StatType))]
    [InlineData(typeof(Avalon.Domain.World.StatType), typeof(Avalon.Network.Packets.Items.ItemStat))]
    [InlineData(typeof(Avalon.Domain.World.ItemBaseStat), typeof(Avalon.Network.Packets.Items.BaseStat))]
    [InlineData(typeof(Avalon.Domain.World.ItemBaseStat), typeof(Avalon.Api.Contract.ItemBaseStat))]
    [InlineData(typeof(Avalon.Network.Packets.Combat.DamageType), typeof(Avalon.Api.Contract.DamageType))]
    [InlineData(typeof(Avalon.World.Public.Enums.SpellRange), typeof(Avalon.Api.Contract.SpellRange))]
    [InlineData(typeof(Avalon.Network.Packets.Abilities.AbilityAmountKind), typeof(Avalon.Api.Contract.AbilityAmountKind))]
    [InlineData(typeof(Avalon.Domain.World.ItemTemplateFlags), typeof(Avalon.Api.Contract.ItemTemplateFlags))]
    [InlineData(typeof(Avalon.Domain.World.QuestObjectiveType), typeof(Avalon.Api.Contract.QuestObjectiveType))]
    [InlineData(typeof(Avalon.Domain.Characters.CharacterQuestState), typeof(Avalon.Api.Contract.CharacterQuestState))]
    [InlineData(typeof(Avalon.Domain.World.AuraKind), typeof(Avalon.Api.Contract.AuraKind))]
    [InlineData(typeof(Avalon.Domain.World.AuraPeriodicKind), typeof(Avalon.Api.Contract.AuraPeriodicKind))]
    [InlineData(typeof(Avalon.Domain.World.AuraStacking), typeof(Avalon.Api.Contract.AuraStacking))]
    [InlineData(typeof(Avalon.Domain.World.AuraStat), typeof(Avalon.Api.Contract.AuraStat))]
    [InlineData(typeof(Avalon.Domain.World.AuraModifierKind), typeof(Avalon.Api.Contract.AuraModifierKind))]
    public void Keep_enum_members_in_step_with_the_contract(Type source, Type target)
    {
        Assert.Equal(Enum.GetValues(source).Length, Enum.GetValues(target).Length);
        foreach (object member in Enum.GetValues(source))
        {
            object cast = Enum.ToObject(target, Convert.ToUInt64(member));
            Assert.Equal(Enum.GetName(source, member), Enum.GetName(target, cast));
        }
    }
}
