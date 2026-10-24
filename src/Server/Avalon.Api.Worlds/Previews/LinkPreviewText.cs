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
    private const string Local = "Local";
    private const string Added = "Added";

    public static string Describe(PublicItemDto item)
    {
        List<string> parts = [SplitWords(item.Rarity.ToString())];
        if (item.Slot is { } slot) parts.Add(SplitWords(slot.ToString()));
        parts.Add(SplitWords(item.SubClass.ToString()));
        parts.AddRange(BaseStats(item));
        parts.AddRange(item.ModifierSpans.Select(s => FormatSpan(s.Stat, s.Min, s.Max, s.Min2, s.Max2)));
        parts.AddRange(item.Stats.Where(s => s.Type == StatType.Armor).Select(s => FormatStat(s.Type, s.Value)));
        parts.AddRange(item.Stats.Where(s => s.Type != StatType.Armor).Select(s => FormatStat(s.Type, s.Value)));
        if (item.RequiredLevel is > 0) parts.Add($"Requires Level {Num(item.RequiredLevel.Value)}");
        if (Attributes(item) is { } attributes) parts.Add(attributes);
        if (Restricted(item.AllowedClasses) is { } classes) parts.Add($"Classes: {classes}");
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
        string kept = line[..keep].TrimEnd();
        // A cut inside a separator would leave it dangling before the ellipsis.
        string separator = Separator.TrimEnd();
        if (kept.EndsWith(separator, StringComparison.Ordinal)) kept = kept[..^separator.Length].TrimEnd();
        return kept + "…";
    }

    /// <summary>A name or description as one line.</summary>
    public static string OneLine(string text) => Whitespace().Replace(text, " ").Trim();

    /// <summary>
    /// The attribute requirements, or null for none: each its own part (<c>Requires 12 Strength · Requires 8 Agility</c>),
    /// or, when any one is enough (#1179), one part (<c>Requires 30 Strength or 30 Intellect</c>).
    /// </summary>
    private static string? Attributes(PublicItemDto item)
    {
        List<string> required = [];
        if (item.RequiredStrength is > 0) required.Add($"{Num(item.RequiredStrength.Value)} Strength");
        if (item.RequiredAgility is > 0) required.Add($"{Num(item.RequiredAgility.Value)} Agility");
        if (item.RequiredIntellect is > 0) required.Add($"{Num(item.RequiredIntellect.Value)} Intellect");
        if (required.Count == 0) return null;
        return item.RequiresAnyAttribute
            ? $"Requires {string.Join(" or ", required)}"
            : string.Join(Separator, required.Select(r => $"Requires {r}"));
    }

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
    /// resistances, the crit multiplier, attack, cast and movement speed and cooldown recovery as percent points
    /// (<c>+2% Crit</c>, <c>+5% Fire Resistance</c>, <c>+5% Movement Speed</c>); the <c>Increased…</c> stats as
    /// <c>+10% increased Fire Damage</c> (increased maximum life as <c>+5% increased Health</c>), and the item-local ones
    /// (#1179) alike (<c>+5% increased Armor</c>, <c>+5% increased Attack Speed</c>); added damage as <c>Adds 5 Fire Damage</c>, <c>Adds 5 Cold Damage to Attacks</c> or <c>Adds 5
    /// Lightning Damage to Spells</c>; the rest, Evasion and Ward among them, as a plain rating (<c>+12 Evasion</c>).
    /// A negative value keeps its own sign (<c>-3% Cold Resistance</c>), and armour never gets a plus. The Dashboard's
    /// packages/shared/src/items/stat-wording.json holds a row for every stat; both sides test against it (#460).
    /// </summary>
    public static string FormatStat(StatType type, long value) =>
        Word(type, value.ToString(CultureInfo.InvariantCulture), negative: value < 0);

    /// <summary>
    /// A stat line a copy rolls within (#1179), as <see cref="FormatStat" /> words the stat with the range for its number:
    /// <c>7-9 Armor</c>, <c>+24-36 Health</c>, <c>+3-5% Crit</c>, one number when both ends are equal; an "adds X to Y"
    /// line, which has a second range for its high end, as <c>Adds 2 to 3-5 Physical Damage to Attacks</c>. The shared
    /// wording file's <c>spans</c> rows hold it.
    /// </summary>
    public static string FormatSpan(StatType type, int min, int max, int? min2, int? max2)
    {
        string amount = Range(min, max);
        if (min2 is { } low2 && max2 is { } high2) amount = $"{amount} to {Range(low2, high2)}";
        return Word(type, amount, negative: min < 0);
    }

    /// <summary>
    /// A base stat every copy rolls within (#1179), its range as the item's own number: <c>4-6 Armor</c>,
    /// <c>19-29 Evasion</c>, <c>6-8 Ward</c>, <c>4-6% Block</c>, an attack time in seconds at both ends
    /// (<c>0.95-1.05 sec attack time</c>); one number when both ends are equal. A weapon's damage ends are worded
    /// together, by <see cref="FormatDamage" />. The shared wording file's <c>baseStats</c> rows hold it.
    /// </summary>
    public static string FormatBaseStat(ItemBaseStat stat, int min, int max) => stat switch
    {
        ItemBaseStat.Armour => $"{Range(min, max)} Armor",
        ItemBaseStat.Evasion => $"{Range(min, max)} Evasion",
        ItemBaseStat.Ward => $"{Range(min, max)} Ward",
        ItemBaseStat.BlockChance => $"{Range(min, max)}% Block",
        ItemBaseStat.AttackTime => min == max
            ? $"{SecondsNumber(min)} sec attack time"
            : $"{SecondsNumber(min)}-{SecondsNumber(max)} sec attack time",
        _ => throw new ArgumentOutOfRangeException(nameof(stat), stat, "A weapon's damage ends are worded by FormatDamage."),
    };

    /// <summary>
    /// A weapon's damage every copy rolls within (#1179): its low end's range, then its high end's, then its type,
    /// <c>2-4 to 6-8 Physical Damage</c>; one number for a range whose ends are equal. The shared wording file's
    /// <c>baseStats</c> rows hold it.
    /// </summary>
    public static string FormatDamage(int min, int max, int min2, int max2, DamageType? type) =>
        type is { } damageType
            ? $"{Range(min, max)} to {Range(min2, max2)} {damageType} Damage"
            : $"{Range(min, max)} to {Range(min2, max2)} Damage";

    /// <summary>
    /// The base stats every copy rolls within (#1179), in <see cref="ItemBaseStat" /> order: a weapon's two damage ends
    /// as one part where its low end stands, and its attack time after it. A lone damage end, which the world refuses,
    /// says nothing.
    /// </summary>
    private static IEnumerable<string> BaseStats(PublicItemDto item)
    {
        PublicItemBaseStatDto? high = item.BaseStats.Find(s => s.Stat == ItemBaseStat.DamageMax);
        foreach (PublicItemBaseStatDto stat in item.BaseStats.OrderBy(s => s.Stat))
        {
            if (stat.Stat == ItemBaseStat.DamageMin && high is not null)
                yield return FormatDamage(stat.Min, stat.Max, high.Min, high.Max, item.DamageType);
            else if (stat.Stat is not (ItemBaseStat.DamageMin or ItemBaseStat.DamageMax))
                yield return FormatBaseStat(stat.Stat, stat.Min, stat.Max);
        }
    }

    /// <summary>
    /// The stat's wording around <paramref name="amount" />, the number as written: a plus before it unless
    /// <paramref name="negative" /> (its own sign is in it), never on armour or added damage.
    /// </summary>
    private static string Word(StatType type, string amount, bool negative)
    {
        string name = type.ToString();
        if (type == StatType.Armor) return $"{amount} Armor";
        string signed = negative ? amount : $"+{amount}";
        // The shared wording calls life Health (#1179).
        if (type == StatType.IncreasedMaximumLife) return $"{signed}% increased Health";
        if (name.EndsWith("Pct", StringComparison.Ordinal)) return $"{signed}% {SplitWords(name[..^3])}";
        if (name.EndsWith("Resistance", StringComparison.Ordinal) || IsPercentPoints(type))
            return $"{signed}% {SplitWords(name)}";
        if (name.StartsWith(Increased, StringComparison.Ordinal) && name.Length > Increased.Length)
            return $"{signed}% increased {SplitWords(name[Increased.Length..])}";
        if (name.StartsWith(Local, StringComparison.Ordinal))
        {
            string local = name[Local.Length..];
            if (local.StartsWith(Increased, StringComparison.Ordinal)) local = local[Increased.Length..];
            return $"{signed}% increased {SplitWords(local)}";
        }

        // Added damage takes no plus sign, and its target a small "to": "Adds 5 Fire Damage to Attacks".
        if (name.StartsWith(Added, StringComparison.Ordinal))
            return $"Adds {amount} {SplitWords(name[Added.Length..]).Replace(" To ", " to ", StringComparison.Ordinal)}";

        return $"{signed} {SplitWords(name)}";
    }

    /// <summary>
    /// A weapon's pace before attack speed (#1178) as <c>2.6 sec attack time</c>, on its damage line; null, so left out,
    /// for no attack time or 0.
    /// </summary>
    public static string? AttackTime(uint? ms) => ms is > 0 ? $"{Seconds(ms.Value)} attack time" : null;

    /// <summary>
    /// The stats without a <c>Pct</c> or <c>Resistance</c> in their name that the world adds as percent points (#1178;
    /// movement speed into MovementSpeedPct, #1189), written with a % and their own name.
    /// </summary>
    private static bool IsPercentPoints(StatType type) =>
        type is StatType.CritMultiplier or StatType.AttackSpeed or StatType.CastSpeed or StatType.CooldownRecovery
            or StatType.MovementSpeed;

    private static string Cost(PublicAbilityDto ability) =>
        ability.Cost == 0
            ? "Free"
            : $"{Num(ability.Cost)} {(ability.CostPowerType is PowerType.Mana or PowerType.Fury or PowerType.Energy ? ability.CostPowerType.ToString() : "power")}";

    /// <summary>
    /// Milliseconds as <c>1.5 sec</c>, as the tooltip writes them: to the hundredth, a half rounded up (<c>1005</c> is
    /// <c>1.01 sec</c>), with no trailing zeros. Decimal, so a tie is a tie, never a binary fraction either side of one.
    /// </summary>
    private static string Seconds(uint ms) => $"{SecondsNumber(ms)} sec";

    /// <summary>Milliseconds as the number of seconds <see cref="Seconds" /> writes: <c>1.5</c>, <c>0.95</c>.</summary>
    private static string SecondsNumber(long ms) =>
        Math.Round(ms / 1000m, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture);

    private static string Num(uint value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Num(ushort value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A percentage as the tooltip writes it: <c>315</c>, <c>0.7</c>.</summary>
    private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary><c>2-4</c>, or one number when both ends are the same.</summary>
    private static string Range(uint min, uint max) => min == max ? Num(max) : $"{Num(min)}-{Num(max)}";

    /// <summary><c>7-9</c>, or one number when both ends are the same.</summary>
    private static string Range(int min, int max) =>
        min == max
            ? min.ToString(CultureInfo.InvariantCulture)
            : $"{min.ToString(CultureInfo.InvariantCulture)}-{max.ToString(CultureInfo.InvariantCulture)}";

    private static string Lower(DamageType type) => type.ToString().ToLowerInvariant();

    /// <summary><c>MainHand</c> as <c>Main Hand</c>.</summary>
    private static string SplitWords(string name) => SplitRegex().Replace(name, "${lower} ${upper}");

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Whitespace();

    [GeneratedRegex("(?<lower>[a-z])(?<upper>[A-Z])", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SplitRegex();
}
