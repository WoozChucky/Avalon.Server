using Avalon.Common;

namespace Avalon.Domain.World;

/// <summary>
/// What base stats each base carries (#1179), the one rule the world, the API and the seed tests share: a template carries
/// exactly the stats of its kind (<see cref="Of" />), each range sound, and a weapon its damage type
/// (<see cref="Problem" />). The world leaves out a template that breaks it (ItemsPatch.Build); the API's template edit
/// refuses one.
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
    /// Why <paramref name="template" />'s base stats break its kind, or null when they hold: it carries exactly
    /// <see cref="Of" />'s stats, once each; every range runs from at least 0 up to its maximum; an attack time is at
    /// least 1 ms; a weapon's highest minimum stays at or under its lowest maximum; and its damage type is set exactly
    /// when it is a weapon kind.
    /// </summary>
    public static string? Problem(ItemTemplate template)
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

        bool weapon = IsWeapon(template.BaseKind);
        if (weapon != (template.DamageType1 is not null))
        {
            return weapon
                ? $"a {kind} template has no damage type"
                : $"a {kind} template has a damage type, which only a weapon has";
        }

        return null;
    }
}
