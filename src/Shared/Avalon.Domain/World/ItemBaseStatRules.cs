using Avalon.Common;

namespace Avalon.Domain.World;

/// <summary>
/// What base stats each base carries (#1179), the one rule the world, the API and the seed tests share: a template carries
/// exactly the stats of its kind (<see cref="Of" />), each range sound, and a Common weapon its damage type
/// (<see cref="Problem" />); and a unique the base it names (<see cref="NamedBaseProblem" />). The world leaves out a
/// template that breaks them (ItemsPatch.Build); the API's template edit refuses one.
/// </summary>
public static class ItemBaseStatRules
{
    private static readonly ItemBaseStat[] s_none = [];
    private static readonly ItemBaseStat[] s_armour = [ItemBaseStat.Armour];
    private static readonly ItemBaseStat[] s_evasion = [ItemBaseStat.Evasion];
    private static readonly ItemBaseStat[] s_ward = [ItemBaseStat.Ward];
    private static readonly ItemBaseStat[] s_shield = [ItemBaseStat.Armour, ItemBaseStat.BlockChance];
    private static readonly ItemBaseStat[] s_weapon = [ItemBaseStat.DamageMin, ItemBaseStat.DamageMax, ItemBaseStat.AttackTime];

    /// <summary>
    /// The base stats a template of <paramref name="kind" /> and <paramref name="rarity" /> carries, in
    /// <see cref="ItemBaseStat" /> order: a Common plate piece armour, leather evasion, cloth ward, a shield armour and
    /// block, a caster off-hand ward, a weapon its damage range and attack time; jewellery and trinkets none. A Unique
    /// template, or one that is no base, carries none: a unique inherits its named base's.
    /// </summary>
    public static IReadOnlyList<ItemBaseStat> Of(ItemBaseKind? kind, ItemRarity rarity)
    {
        if (rarity != ItemRarity.Common || kind is not { } k)
            return s_none;

        return k switch
        {
            >= ItemBaseKind.PlateHead and <= ItemBaseKind.PlateFeet => s_armour,
            >= ItemBaseKind.LeatherHead and <= ItemBaseKind.LeatherFeet => s_evasion,
            >= ItemBaseKind.ClothHead and <= ItemBaseKind.ClothFeet => s_ward,
            ItemBaseKind.Shield => s_shield,
            ItemBaseKind.CasterOffHand => s_ward,
            _ when IsWeapon(k) => s_weapon,
            _ => s_none,
        };
    }

    /// <summary>True for a kind that is a weapon: a sword, a mace, a staff or a bow, the kinds with a damage type.</summary>
    public static bool IsWeapon(ItemBaseKind? kind) =>
        kind is ItemBaseKind.Sword or ItemBaseKind.Mace or ItemBaseKind.Staff or ItemBaseKind.Bow;

    /// <summary>
    /// Why <paramref name="template" />'s base stats break its kind, or null when they hold: its ranges hold
    /// (<see cref="RangesProblem" />) and its damage type is set exactly when it is a Common weapon
    /// (<see cref="DamageTypeProblem" />).
    /// </summary>
    public static string? Problem(ItemTemplate template) => RangesProblem(template) ?? DamageTypeProblem(template);

    /// <summary>
    /// Why <paramref name="template" />'s base-stat ranges break its kind, or null when they hold: it carries exactly
    /// <see cref="Of" />'s stats, once each; every range runs from at least 0 up to its maximum; an attack time is at
    /// least 1 ms; and a weapon's highest minimum stays at or under its lowest maximum.
    /// </summary>
    public static string? RangesProblem(ItemTemplate template)
    {
        IReadOnlyList<ItemBaseStat> expected = Of(template.BaseKind, template.Rarity);
        List<ItemTemplateBaseStat> rows = template.BaseStats;
        string kind = template.BaseKind?.ToString() ?? "no base kind";

        if (rows.Count != expected.Count || !expected.All(stat => rows.Count(row => row.Stat == stat) == 1))
        {
            string carried = rows.Count == 0 ? "none" : string.Join(", ", rows.Select(row => row.Stat).Order());
            string asked = expected.Count == 0 ? "none" : string.Join(", ", expected);
            return $"a {template.Rarity} {kind} template carries base stats {asked}, not {carried}";
        }

        foreach (ItemTemplateBaseStat row in rows)
        {
            if (row.Min < 0 || row.Min > row.Max)
                return $"its {row.Stat} range {row.Min}-{row.Max} does not run from at least 0 up to its maximum";
        }

        if (rows.Find(row => row.Stat == ItemBaseStat.AttackTime) is { Min: < 1 } attackTime)
            return $"its attack time range {attackTime.Min}-{attackTime.Max} starts below 1 ms";

        if (rows.Find(row => row.Stat == ItemBaseStat.DamageMin) is { } low
            && rows.Find(row => row.Stat == ItemBaseStat.DamageMax) is { } high
            && low.Max > high.Min)
        {
            return $"its minimum damage {low.Min}-{low.Max} reaches past its maximum damage {high.Min}-{high.Max}";
        }

        return null;
    }

    /// <summary>
    /// Why <paramref name="template" />'s damage type breaks its kind, or null when it holds: a Common template of a weapon
    /// kind names the type of its damage, and no other template names one. A unique weapon deals its named base's type, as
    /// it rolls its base's ranges, so it carries none of its own.
    /// </summary>
    public static string? DamageTypeProblem(ItemTemplate template)
    {
        string kind = template.BaseKind?.ToString() ?? "no base kind";
        bool typed = template.Rarity == ItemRarity.Common && IsWeapon(template.BaseKind);
        if (typed == (template.DamageType is not null))
            return null;

        if (typed)
            return $"a {kind} template has no damage type";

        return template.Rarity == ItemRarity.Unique && IsWeapon(template.BaseKind)
            ? $"a Unique {kind} template has a damage type, which it takes from its base"
            : $"a {kind} template has a damage type, which only a weapon has";
    }

    /// <summary>
    /// Why <paramref name="template" /> may not name <paramref name="named" /> as its base (its
    /// <see cref="ItemTemplate.BaseTemplateId" />; null when it names none, or names a template that is missing or left
    /// out), or null when it may: only a Unique template names a base, and every one does, a Common template of the
    /// unique's kind whose required level is at or under the unique's. The world leaves out a template that breaks it
    /// (ItemsPatch.Build), and the API's template edit refuses an edit of the unique, or of its base, that would.
    /// </summary>
    public static string? NamedBaseProblem(ItemTemplate template, ItemTemplate? named) => template.Rarity switch
    {
        not ItemRarity.Unique => template.BaseTemplateId is null ? null : "only a unique template names a base",
        _ => named switch
        {
            null => template.BaseTemplateId is { } id
                ? $"its base {id.Value} is missing or left out"
                : "a unique template names no base",
            { Rarity: not ItemRarity.Common } or { BaseKind: null } => $"its base {named.Id.Value} is not a common base",
            _ when named.BaseKind != template.BaseKind =>
                $"its base {named.Id.Value} is a {named.BaseKind}, not a {template.BaseKind}",
            _ when (named.RequiredLevel ?? 1) > (template.RequiredLevel ?? 1) =>
                $"its base {named.Id.Value} requires level {named.RequiredLevel ?? 1}, above the unique's {template.RequiredLevel ?? 1}",
            _ => null,
        },
    };
}
