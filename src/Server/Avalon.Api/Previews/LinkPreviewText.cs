using System.Globalization;
using System.Text.RegularExpressions;
using Avalon.Api.Contract;
using Avalon.World.Public.Enums;

namespace Avalon.Api.Previews;

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
        return Cap(string.Join(Separator, parts));
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
        List<CharacterClass> distinct = allowed.Distinct().ToList();
        return distinct.Count == 0 || distinct.Count >= Enum.GetValues<CharacterClass>().Length
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

    /// <summary>The tooltip's base-and-scaling line (formatAbilityScaling); abilities carry no other text.</summary>
    private static string? Amount(PublicAbilityDto a)
    {
        if (a.AmountKind == AbilityAmountKind.None) return null;
        List<string> terms = [Num(a.EffectValue)];
        if (a.ScalingCoefficient > 0)
            terms.Add($"{Percent(a.ScalingCoefficient)} of {(a.ScalingStat == AbilityScalingStat.Ability ? "Ability" : "Attack")} Damage");
        if (a.BaseDamageCoefficient > 0) terms.Add($"{Percent(a.BaseDamageCoefficient)} of weapon damage");
        string line = string.Join(" + ", terms);
        if (a.AmountKind == AbilityAmountKind.Healing) return $"Heals {line}";
        return terms.Count == 1 ? $"{line} damage" : line;
    }

    private static string Percent(float coefficient) =>
        $"{Math.Round(coefficient * 100, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)}%";

    private static string Num(uint value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Num(ushort value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary><c>MainHand</c> as <c>Main Hand</c>.</summary>
    private static string SplitWords(string name) => SplitRegex().Replace(name, "$1 $2");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex("([a-z])([A-Z])")]
    private static partial Regex SplitRegex();
}
