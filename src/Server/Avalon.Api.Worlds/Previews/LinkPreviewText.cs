using System.Globalization;
using System.Text.RegularExpressions;
using Avalon.Api.Contract;
using Avalon.Common;

namespace Avalon.Api.Worlds.Previews;

/// <summary>
/// The one-line description and colours of a link preview. The wording and colours mirror the public
/// site's tooltips (Avalon.Dashboard apps/public/src/components/game/ItemTooltip.tsx, AbilityTooltip.tsx,
/// rarity.ts and packages/shared/src/items/format.ts, abilities/*.ts), so a preview reads like the tooltip.
/// </summary>
public static partial class LinkPreviewText
{
    /// <summary>The longest description, ellipsis included.</summary>
    public const int MaxDescriptionLength = 200;

    private const string Separator = " · ";

    private const string Increased = "Increased";

    public static string Describe(PublicItemDto item)
    {
        List<string> parts = [SplitWords(item.Rarity.ToString())];
        if (item.Slot is { } slot) parts.Add(SplitWords(slot.ToString()));
        parts.Add(SplitWords(item.SubClass.ToString()));
        parts.AddRange(item.Damage.Select(d =>
            string.Join(' ', new[] { d.Min == d.Max ? Num(d.Max) : $"{Num(d.Min)} – {Num(d.Max)}", d.Type?.ToString(), "Damage" }
                .Where(p => !string.IsNullOrEmpty(p)))));
        if (item.AttackTimeMs is > 0) parts.Add(AttackTime(item.AttackTimeMs.Value));
        parts.AddRange(item.Stats.Where(s => s.Type == StatType.Armor).Select(s => FormatStat(s.Type, s.Value)));
        parts.AddRange(item.Stats.Where(s => s.Type != StatType.Armor).Select(s => FormatStat(s.Type, s.Value)));
        if (item.RequiredLevel is > 0) parts.Add($"Requires Level {Num(item.RequiredLevel.Value)}");
        if (item.RequiredStrength is > 0) parts.Add($"Requires {Num(item.RequiredStrength.Value)} Strength");
        if (item.RequiredAgility is > 0) parts.Add($"Requires {Num(item.RequiredAgility.Value)} Agility");
        if (item.RequiredIntellect is > 0) parts.Add($"Requires {Num(item.RequiredIntellect.Value)} Intellect");
        if (Restricted(item.AllowedClasses) is { } classes) parts.Add($"Classes: {classes}");
        if (item.ItemPower is > 0) parts.Add($"Item Power {Num(item.ItemPower.Value)}");
        return Cap(string.Join(Separator, parts));
    }

    public static string Describe(PublicAbilityDto ability)
    {
        List<string> parts = [];
        if (Restricted(ability.AllowedClasses) is { } classes) parts.Add(classes);
        parts.Add(Cost(ability));
        parts.Add(ability.CastTime == 0 ? "Instant" : $"{Seconds(ability.CastTime)} cast");
        if (ability.Cooldown > 0) parts.Add($"{Seconds(ability.Cooldown)} cooldown");
        if (Amount(ability) is { } amount) parts.Add(amount);
        else if (ability.DamageType is { } type) parts.Add($"{type} damage");
        return Cap(string.Join(Separator, parts));
    }

    /// <summary>
    /// What one hit deals or heals (#1178), or null for an ability with no direct amount (it only applies an aura) or no
    /// kind to say it by: an Attack as its weapon damage (<c>315 % of weapon damage, converts 50 % to cold</c>, <c>adds
    /// 2-4 blight damage</c>), or a creature's (no classes) as its natural damage, with its type when that is not
    /// physical (<c>240 % of its natural damage, blight damage</c>); a Spell or a Heal as its level-1 base and its growth
    /// (<c>14-20 lightning damage, +0.7 % per level</c>, <c>Heals 30-44</c>).
    /// </summary>
    private static string? Amount(PublicAbilityDto ability)
    {
        if (ability.AmountKind == AbilityAmountKind.Damage && ability.Tags.HasFlag(AbilityTags.Attack))
        {
            string of = ability.AllowedClasses.Count == 0 ? "of its natural damage" : "of weapon damage";
            List<string> attack = [$"{Number(ability.DamageEffectiveness)} % {of}"];
            if (ability.DamageType is { } type && type != DamageType.Physical)
                attack.Add($"{Lower(type)} damage");
            if (ability.ConvertPct > 0f && ability.ConvertToType is { } to)
                attack.Add($"converts {Number(ability.ConvertPct)} % to {Lower(to)}");
            if (ability.AddedMax > 0 && ability.AddedType is { } added)
                attack.Add($"adds {Range(ability.AddedMin, ability.AddedMax)} {Lower(added)} damage");
            return string.Join(", ", attack);
        }

        string? spell = ability.AmountKind switch
        {
            AbilityAmountKind.Damage when ability.Tags.HasFlag(AbilityTags.Spell) && ability.DamageType is { } type =>
                $"{Range(ability.SpellBaseMin, ability.SpellBaseMax)} {Lower(type)} damage",
            AbilityAmountKind.Healing when ability.Tags.HasFlag(AbilityTags.Heal) =>
                $"Heals {Range(ability.SpellBaseMin, ability.SpellBaseMax)}",
            _ => null,
        };
        return spell is not null && ability.SpellBasePerLevelPct > 0f
            ? $"{spell}, +{Number(ability.SpellBasePerLevelPct)} % per level"
            : spell;
    }

    /// <summary>One line, no more than <see cref="MaxDescriptionLength"/> characters, cut with an ellipsis.</summary>
    public static string Cap(string text)
    {
        string line = OneLine(text);
        if (line.Length <= MaxDescriptionLength) return line;
        int keep = MaxDescriptionLength - 1;
        if (char.IsHighSurrogate(line[keep - 1])) keep--;
        return line[..keep].TrimEnd() + "…";
    }

    /// <summary>A name or description as one line.</summary>
    public static string OneLine(string text) => Whitespace().Replace(text, " ").Trim();

    /// <summary>The classes as a list, or null when every class (or none listed) may use it.</summary>
    private static string? Restricted(List<CharacterClass> allowed)
    {
        var distinct = allowed.Distinct().ToList();
        return distinct.Count == 0 || distinct.Count >= CharacterClasses.Playable.Count
            ? null
            : string.Join(", ", distinct);
    }

    /// <summary>
    /// One item stat as the tooltip writes it (#1189): armour as a plain <c>8 Armor</c>; the <c>…Pct</c> stats, the
    /// resistances, the crit multiplier and the speeds as percent points (<c>+2% Crit</c>, <c>+5% Fire Resistance</c>,
    /// <c>+5% Cast Speed</c>); the <c>Increased…</c> stats as <c>+10% increased Fire Damage</c>; every other stat, Evasion
    /// and Ward included, as a plain rating (<c>+12 Evasion</c>). schema/items/stat-wording-v1.json holds a row for every
    /// stat, which the public site's tooltip tests against too.
    /// </summary>
    public static string FormatStat(StatType type, uint value)
    {
        string name = type.ToString();
        string amount = Num(value);
        if (type == StatType.Armor) return $"{amount} Armor";
        if (name.EndsWith("Pct", StringComparison.Ordinal)) return $"+{amount}% {SplitWords(name[..^3])}";
        if (name.EndsWith("Resistance", StringComparison.Ordinal) || IsPercentPoints(type))
            return $"+{amount}% {SplitWords(name)}";
        if (name.StartsWith(Increased, StringComparison.Ordinal) && name.Length > Increased.Length)
            return $"+{amount}% increased {SplitWords(name[Increased.Length..])}";
        return $"+{amount} {SplitWords(name)}";
    }

    /// <summary>A weapon's pace before attack speed (#1178) as <c>2.6 sec attack time</c>, on its damage line.</summary>
    public static string AttackTime(uint ms) => $"{Seconds(ms)} attack time";

    /// <summary>The stats that are percentages already (#1178) and keep their own name.</summary>
    private static bool IsPercentPoints(StatType type) =>
        type is StatType.CritMultiplier or StatType.AttackSpeed or StatType.CastSpeed or StatType.CooldownRecovery;

    private static string Cost(PublicAbilityDto ability) =>
        ability.Cost == 0
            ? "Free"
            : $"{Num(ability.Cost)} {(ability.CostPowerType is PowerType.Mana or PowerType.Fury or PowerType.Energy ? ability.CostPowerType.ToString() : "power")}";

    /// <summary>Milliseconds as <c>1.5 sec</c>, as the tooltip writes them.</summary>
    private static string Seconds(uint ms) =>
        $"{(ms / 1000.0).ToString("0.##", CultureInfo.InvariantCulture)} sec";

    private static string Num(uint value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Num(ushort value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A percentage as the tooltip writes it: <c>315</c>, <c>0.7</c>.</summary>
    private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary><c>2-4</c>, or one number when both ends are the same.</summary>
    private static string Range(uint min, uint max) => min == max ? Num(max) : $"{Num(min)}-{Num(max)}";

    private static string Lower(DamageType type) => type.ToString().ToLowerInvariant();

    /// <summary><c>MainHand</c> as <c>Main Hand</c>.</summary>
    private static string SplitWords(string name) => SplitRegex().Replace(name, "${lower} ${upper}");

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Whitespace();

    [GeneratedRegex("(?<lower>[a-z])(?<upper>[A-Z])", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SplitRegex();
}
