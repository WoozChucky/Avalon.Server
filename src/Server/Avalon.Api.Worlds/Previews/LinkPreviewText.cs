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

    public static string Describe(PublicItemDto item)
    {
        List<string> parts = [SplitWords(item.Rarity.ToString())];
        if (item.Slot is { } slot) parts.Add(SplitWords(slot.ToString()));
        parts.Add(SplitWords(item.SubClass.ToString()));
        parts.AddRange(item.Damage.Select(d =>
            string.Join(' ', new[] { d.Min == d.Max ? Num(d.Max) : $"{Num(d.Min)} – {Num(d.Max)}", d.Type?.ToString(), "Damage" }
                .Where(p => !string.IsNullOrEmpty(p)))));
        parts.AddRange(item.Stats.Where(s => s.Type == StatType.Armor).Select(s => $"{Num(s.Value)} Armor"));
        parts.AddRange(item.Stats.Where(s => s.Type != StatType.Armor).Select(FormatStat));
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
    /// 2-4 blight damage</c>), a Spell or a Heal as its level-1 base and its growth (<c>14-20 lightning damage, +0.7 % per
    /// level</c>, <c>Heals 30-44</c>).
    /// </summary>
    private static string? Amount(PublicAbilityDto ability)
    {
        if (ability.AmountKind == AbilityAmountKind.Damage && ability.Tags.HasFlag(AbilityTags.Attack))
        {
            List<string> attack = [$"{Number(ability.DamageEffectiveness)} % of weapon damage"];
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

    private static string FormatStat(PublicItemStatDto stat)
    {
        string type = stat.Type.ToString();
        if (type.EndsWith("Pct", StringComparison.Ordinal))
            return $"+{Num(stat.Value)}% {SplitWords(type[..^3])}";
        return $"+{Num(stat.Value)} {SplitWords(type)}";
    }

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
