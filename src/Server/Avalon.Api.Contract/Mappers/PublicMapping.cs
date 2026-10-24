using Avalon.Combat;
using Avalon.Domain.World;
using DomainFlags = Avalon.Domain.World.ItemTemplateFlags;

namespace Avalon.Api.Contract.Mappers;

public static class PublicMapping
{
    /// <param name="linked">
    /// The modifiers every copy of the template rolls (#1179), in link order: the world catalog's
    /// <c>LinkedTo(t.Id)</c>, a base's implicits or a unique's own modifier.
    /// </param>
    public static PublicItemDto ToPublicDto(this ItemTemplate t, IReadOnlyList<ItemModifier> linked) => new()
    {
        Id = t.Id.Value,
        Name = t.Name ?? "",
        Rarity = (ItemRarity)t.Rarity,
        Class = (ItemClass)t.Class,
        SubClass = (ItemSubClass)t.SubClass,
        Slot = (ItemSlotType?)t.Slot,
        RequiredLevel = t.RequiredLevel,
        RequiredStrength = t.RequiredStrength,
        RequiredAgility = t.RequiredAgility,
        RequiredIntellect = t.RequiredIntellect,
        RequiresAnyAttribute = t.RequiresAnyAttribute,
        AttackTimeMs = t.AttackTimeMs,
        AllowedClasses = t.AllowedClasses?.ToList() ?? [],
        MaxStackSize = t.MaxStackSize,
        SellPrice = t.SellPrice,
        CannotBeSold = t.Flags.HasFlag(DomainFlags.NoSell),
        Attunes = AttunementOf(t.Flags),
        Unique = t.Flags.HasFlag(DomainFlags.UniqueEquipped) ? ItemUniqueness.UniqueEquipped
            : t.Flags.HasFlag(DomainFlags.Unique) ? ItemUniqueness.Unique
            : ItemUniqueness.None,
        Damage = DamageOf(t).ToList(),
        Stats = StatsOf(t).ToList(),
        ModifierSpans = [.. linked.SelectMany(SpansOf)],
    };

    public static PublicAbilityDto ToPublicDto(this AbilityTemplate t) => new()
    {
        Id = t.Id.Value,
        Name = t.Name ?? "",
        Cost = t.Cost,
        CostPowerType = (PowerType)t.CostPowerType,
        Range = (SpellRange)t.Range,
        CastTime = t.CastTime,
        Cooldown = t.Cooldown,
        AllowedClasses = t.AllowedClasses?.ToList() ?? [],
        DamageType = (DamageType?)t.DamageType,
        Tags = (AbilityTags)t.Tags,
        DamageEffectiveness = t.DamageEffectiveness,
        SpellBaseMin = t.SpellBaseMin,
        SpellBaseMax = t.SpellBaseMax,
        SpellBasePerLevelPct = t.SpellBasePerLevelPct,
        ConvertToType = (DamageType?)t.ConvertToType,
        ConvertPct = t.ConvertPct,
        AddedType = (DamageType?)t.AddedType,
        AddedMin = t.AddedMin,
        AddedMax = t.AddedMax,
        AmountKind = (AbilityAmountKind)AbilityAmountMath.KindOf(t.ScriptName, t.Affects, t.Effects),
    };

    private static ItemAttunement AttunementOf(DomainFlags flags) =>
        flags.HasFlag(DomainFlags.AttuneOnAccount) ? ItemAttunement.ToAccount
        : flags.HasFlag(DomainFlags.AttuneOnPickup) ? ItemAttunement.OnPickup
        : flags.HasFlag(DomainFlags.AttuneOnEquip) ? ItemAttunement.OnEquip
        : flags.HasFlag(DomainFlags.AttuneOnUse) ? ItemAttunement.OnUse
        : ItemAttunement.None;

    private static IEnumerable<PublicItemDamageDto> DamageOf(ItemTemplate t)
    {
        foreach ((uint? min, uint? max, Avalon.Network.Packets.Combat.DamageType? type) in new[]
                 {
                     (t.DamageMin1, t.DamageMax1, t.DamageType1),
                     (t.DamageMin2, t.DamageMax2, t.DamageType2),
                 })
        {
            if (max is not > 0) continue;
            yield return new PublicItemDamageDto
            {
                Min = Math.Min(min ?? 0, max.Value),
                Max = max.Value,
                Type = (DamageType?)type,
            };
        }
    }

    private static IEnumerable<PublicItemSpanDto> SpansOf(ItemModifier modifier) =>
        modifier.Stats.OrderBy(line => line.Index).Select(line => new PublicItemSpanDto
        {
            Kind = (ItemModifierKind)modifier.Group.Kind,
            Stat = (StatType)line.Stat,
            Min = line.Min,
            Max = line.Max,
            Min2 = line.Min2,
            Max2 = line.Max2,
        });

    private static IEnumerable<PublicItemStatDto> StatsOf(ItemTemplate t)
    {
        (Avalon.Domain.World.StatType? Type, uint? Value)[] pairs =
        [
            (t.StatType1, t.StatValue1), (t.StatType2, t.StatValue2), (t.StatType3, t.StatValue3),
            (t.StatType4, t.StatValue4), (t.StatType5, t.StatValue5), (t.StatType6, t.StatValue6),
            (t.StatType7, t.StatValue7), (t.StatType8, t.StatValue8), (t.StatType9, t.StatValue9),
            (t.StatType10, t.StatValue10),
        ];
        foreach ((Avalon.Domain.World.StatType? type, uint? value) in pairs)
        {
            if (type is { } statType && value is > 0)
                yield return new PublicItemStatDto { Type = (StatType)statType, Value = value.Value };
        }
    }
}
