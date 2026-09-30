using Avalon.Domain.World;
using DomainFlags = Avalon.Domain.World.ItemTemplateFlags;
using Avalon.Combat;

namespace Avalon.Api.Contract.Mappers;

public static class PublicMapping
{
    public static PublicItemDto ToPublicDto(this ItemTemplate t) => new()
    {
        Id = t.Id.Value,
        Name = t.Name ?? "",
        Rarity = (ItemRarity)t.Rarity,
        Class = (ItemClass)t.Class,
        SubClass = (ItemSubClass)t.SubClass,
        Slot = (ItemSlotType?)t.Slot,
        RequiredLevel = t.RequiredLevel,
        ItemPower = t.ItemPower,
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
        AmountKind = (AbilityAmountKind)AbilityAmountMath.KindOf(t.ScriptName, t.Affects),
        EffectValue = t.EffectValue,
        ScalingStat = (AbilityScalingStat)t.ScalingStat,
        ScalingCoefficient = t.ScalingCoefficient,
        BaseDamageCoefficient = t.BaseDamageCoefficient,
    };

    private static ItemAttunement AttunementOf(DomainFlags flags) =>
        flags.HasFlag(DomainFlags.AttuneOnAccount) ? ItemAttunement.ToAccount
        : flags.HasFlag(DomainFlags.AttuneOnPickup) ? ItemAttunement.OnPickup
        : flags.HasFlag(DomainFlags.AttuneOnEquip) ? ItemAttunement.OnEquip
        : flags.HasFlag(DomainFlags.AttuneOnUse) ? ItemAttunement.OnUse
        : ItemAttunement.None;

    private static IEnumerable<PublicItemDamageDto> DamageOf(ItemTemplate t)
    {
        foreach ((uint? min, uint? max, Avalon.Domain.World.DamageType? type) in new[]
                 {
                     (t.DamageMin1, t.DamageMax1, t.DamageType1),
                     (t.DamageMin2, t.DamageMax2, t.DamageType2),
                 })
        {
            if (max is not > 0) continue;
            yield return new PublicItemDamageDto
            {
                Min = Math.Min(min ?? 0, max.Value), Max = max.Value, Type = (DamageType?)type,
            };
        }
    }

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
            if (type is { } statType && value is > 0)
                yield return new PublicItemStatDto { Type = (StatType)statType, Value = value.Value };
    }
}
