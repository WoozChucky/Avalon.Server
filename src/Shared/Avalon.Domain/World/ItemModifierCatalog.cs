using Avalon.Common;
using Avalon.Common.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Avalon.Domain.World;

/// <summary>A prefix or suffix row that may roll on one base kind, with its weight there (above 0).</summary>
public readonly record struct ItemModifierCandidate(ItemModifier Modifier, int Weight);

/// <summary>
/// The modifier catalog as one generation reads it (#1179): every accepted modifier by id, each template's links in
/// index order, and per base kind the prefix and suffix rows that may roll there. Built once, never changed; a reload
/// builds a new one. A row it cannot trust is logged and left out:
/// <list type="bullet">
/// <item>a modifier with no group or no stat line, a prefix, suffix or implicit row without a tier, a unique row with
/// one, a unique or implicit modifier that carries a weight, or a row whose group breaks the Resonance rule (#1180: a
/// prefix or suffix group carries exactly one theme, a unique or implicit group none);</item>
/// <item>a template's links when one names a modifier left out or a prefix or suffix, or when they mix a unique
/// modifier with any other link (a unique template links its one Unique modifier, a common base its one Implicit). The
/// template is then left with no links at all, never with part of them.</item>
/// </list>
/// Which template is unique is the template's own fact: <see cref="RarityRefusal" /> holds the links against it, and the
/// items area (ItemsPatch.Build) leaves out a template whose links break it, with <see cref="WithoutLinksOf" />.
/// </summary>
public sealed class ItemModifierCatalog
{
    /// <summary>How many of a family's weakest tiers a brick rerolls into (#1180 part C): <see cref="BrickTiers" />.</summary>
    public const int BrickTierCount = 3;

    private static readonly int s_kinds = Enum.GetValues<ItemBaseKind>().Max(kind => (int)kind) + 1;

    private readonly Dictionary<int, ItemModifier> _modifiers;
    private readonly Dictionary<ulong, ItemModifier[]> _links;
    private readonly ItemModifierCandidate[][] _candidates;

    // Each tiered row's next better row of its own group (#1180), by the row's id: a row with none has no entry.
    private readonly Dictionary<int, ItemModifier> _nextTier;

    // Each prefix and suffix group's rows of its BrickTierCount weakest tiers (#1180 part C), by the group's id, sorted by
    // tier, the weakest (the largest number) first, then by id.
    private readonly Dictionary<int, ItemModifier[]> _brickRows;

    public ItemModifierCatalog(
        IReadOnlyCollection<ItemModifier> modifiers, IReadOnlyCollection<ItemTemplateModifier> links, ILogger logger)
    {
        _modifiers = new Dictionary<int, ItemModifier>(modifiers.Count);
        foreach (ItemModifier modifier in modifiers)
        {
            if (Refusal(modifier) is { } reason)
            {
                logger.LogError("Item modifier {ModifierId} is left out: {Reason}", modifier.Id, reason);
                continue;
            }

            _modifiers[modifier.Id] = modifier;
        }

        Resonance = new ResonanceTable(_modifiers.Values);
        _nextTier = NextTiers(_modifiers.Values);
        _brickRows = BrickRows(_modifiers.Values);

        _links = [];
        foreach (IGrouping<ulong, ItemTemplateModifier> template in links
                     .GroupBy(link => link.TemplateId.Value)
                     .OrderBy(group => group.Key))
        {
            ItemModifier?[] linked = template.OrderBy(link => link.Index)
                .Select(link => _modifiers.GetValueOrDefault(link.ModifierId))
                .ToArray();
            if (LinkRefusal(linked) is { } reason)
            {
                logger.LogError("Item template {TemplateId} keeps none of its modifier links: {Reason}", template.Key, reason);
                continue;
            }

            _links[template.Key] = linked!;
        }

        _candidates = new ItemModifierCandidate[s_kinds][];
        for (int kind = 0; kind < s_kinds; kind++)
        {
            _candidates[kind] = _modifiers.Values
                .Where(modifier => modifier.Group.Kind is ItemModifierKind.Prefix or ItemModifierKind.Suffix)
                .OrderBy(modifier => modifier.Id)
                .Select(modifier => new ItemModifierCandidate(modifier, WeightOn(modifier, (ItemBaseKind)kind)))
                .Where(candidate => candidate.Weight > 0)
                .ToArray();
        }
    }

    private ItemModifierCatalog(ItemModifierCatalog from, Dictionary<ulong, ItemModifier[]> links)
    {
        _modifiers = from._modifiers;
        _nextTier = from._nextTier;
        _brickRows = from._brickRows;
        _candidates = from._candidates;
        Resonance = from.Resonance;
        _links = links;
    }

    /// <summary>
    /// This catalog without the links of <paramref name="templates" />: the same modifiers, candidates and Resonance table,
    /// shared, and every other template's links. What the items area keeps when a template's links break its rarity.
    /// </summary>
    public ItemModifierCatalog WithoutLinksOf(IReadOnlySet<ulong> templates) =>
        new(this, _links.Where(pair => !templates.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value));

    /// <summary>
    /// The Resonance themes of the accepted prefix and suffix rows (#1180), built with the catalog: each theme's slot and
    /// each affix modifier's. A reload builds a new catalog and with it a new table.
    /// </summary>
    public ResonanceTable Resonance { get; }

    /// <summary>How many modifiers the catalog accepted.</summary>
    public int Count => _modifiers.Count;

    /// <summary>The accepted modifier of that id, with its group, stat lines and weights; null when there is none.</summary>
    public ItemModifier? Find(int modifierId) => _modifiers.GetValueOrDefault(modifierId);

    /// <summary>
    /// The accepted row one tier better than <paramref name="modifier" /> in its own group (#1180): of the same
    /// <see cref="ItemModifier.GroupId" />, the largest <see cref="ItemModifier.Tier" /> below its own (tiers count down,
    /// T1 the best), the lowest id among the rows of that tier that roll on every base kind <paramref name="modifier" />
    /// rolls on, so a rise never lands on a row the item's base could not roll. Null for an untiered row, a row the
    /// catalog does not hold, the best tier of its group, and a row whose next tier rolls on fewer kinds (it polishes
    /// instead). Worked out when the catalog is built, so asking allocates nothing. It knows nothing of item levels: the
    /// wake decides whether the row is within an item's reach.
    /// </summary>
    public ItemModifier? NextTier(ItemModifier modifier) => _nextTier.GetValueOrDefault(modifier.Id);

    /// <summary>
    /// The rows a brick may reroll <paramref name="modifier" /> into on an item of <paramref name="kind" /> at
    /// <paramref name="itemLevel" /> (#1180 part C), written into <paramref name="modifierIds" />, which holds at least
    /// <see cref="BrickTierCount" />: of the <see cref="BrickTierCount" /> weakest tiers the catalog holds for its
    /// <see cref="ItemModifier.GroupId" /> (the largest <see cref="ItemModifier.Tier" /> numbers), weakest first, one id per
    /// tier, the lowest of that tier weighing above 0 on <paramref name="kind" /> whose required item level is at most
    /// <paramref name="itemLevel" />. A tier with no such row is skipped. Answers how many it wrote: 0 for a group that is
    /// not a prefix or suffix, or one the catalog does not hold. Worked out per group when the catalog is built, so asking
    /// allocates nothing.
    /// </summary>
    public int BrickTiers(ItemModifier modifier, ItemBaseKind kind, ushort itemLevel, Span<int> modifierIds)
    {
        if (!_brickRows.TryGetValue(modifier.GroupId, out ItemModifier[]? rows))
            return 0;

        int written = 0;
        byte? done = null;
        foreach (ItemModifier row in rows)
        {
            if (row.Tier == done || row.RequiredItemLevel > itemLevel || WeightOn(row, kind) <= 0)
                continue;

            modifierIds[written++] = row.Id;
            done = row.Tier;
        }

        return written;
    }

    /// <summary>Whether <paramref name="modifier" /> weighs above 0 on <paramref name="kind" />: whether it rolls there.</summary>
    public bool RollsOn(ItemModifier modifier, ItemBaseKind kind) => WeightOn(modifier, kind) > 0;

    /// <summary>The modifiers every copy of the template rolls, in link order; empty when it links none.</summary>
    public IReadOnlyList<ItemModifier> LinkedTo(ItemTemplateId template) =>
        template is not null && _links.TryGetValue(template.Value, out ItemModifier[]? linked) ? linked : [];

    /// <summary>The prefix and suffix rows weighing above 0 on <paramref name="kind" />, by id; empty for none.</summary>
    public IReadOnlyList<ItemModifierCandidate> CandidatesFor(ItemBaseKind kind) =>
        (int)kind >= 0 && (int)kind < _candidates.Length ? _candidates[(int)kind] : [];

    /// <summary>
    /// Why a template of <paramref name="rarity" /> and <paramref name="kind" /> may not link <paramref name="linked" />
    /// (the half of the link rules that needs the template, which the catalog cannot see), or null when it may: a Unique
    /// template links exactly one Unique modifier, a Common base (one with a kind) exactly one Implicit modifier, a Common
    /// template without a kind (a consumable, a quest item) none, and a template of any other rarity none. The world's
    /// items area leaves out a template whose links break it (ItemsPatch.Build), the API's reads drop its links, and a
    /// template edit that would break it is refused.
    /// </summary>
    public static string? RarityRefusal(ItemRarity rarity, ItemBaseKind? kind, IReadOnlyList<ItemModifier> linked) => rarity switch
    {
        ItemRarity.Unique when linked is not [{ Group.Kind: ItemModifierKind.Unique }] =>
            "a unique template links exactly one unique modifier",
        ItemRarity.Common when kind is not null && linked is not [{ Group.Kind: ItemModifierKind.Implicit }] =>
            "a common base links exactly one implicit modifier",
        ItemRarity.Common when kind is null && linked.Count > 0 => "a common template without a base kind links no modifier",
        ItemRarity.Unique or ItemRarity.Common => null,
        _ => linked.Count > 0 ? $"a {rarity} template links no modifier" : null,
    };

    /// <summary>
    /// For each tiered row, the next better row of its group (<see cref="NextTier" />). Each group's rows are sorted best
    /// tier first, then by id; for a row, the tier just before its own is the next better one, and that tier's first row
    /// rolling on every kind the row rolls on, the lowest id, is the one named. The best tier has none.
    /// </summary>
    private static Dictionary<int, ItemModifier> NextTiers(IEnumerable<ItemModifier> accepted)
    {
        var next = new Dictionary<int, ItemModifier>();
        foreach (IGrouping<int, ItemModifier> group in accepted.Where(m => m.Tier is not null).GroupBy(m => m.GroupId))
        {
            ItemModifier[] sorted = [.. group.OrderBy(m => m.Tier).ThenBy(m => m.Id)];
            int betterStart = -1, tierStart = 0;
            for (int i = 1; i < sorted.Length; i++)
            {
                if (sorted[i].Tier != sorted[i - 1].Tier)
                    (betterStart, tierStart) = (tierStart, i);

                if (betterStart < 0)
                    continue;

                for (int b = betterStart; b < tierStart; b++)
                {
                    if (RollsOnEveryKindOf(sorted[b], sorted[i]))
                    {
                        next[sorted[i].Id] = sorted[b];
                        break;
                    }
                }
            }
        }

        return next;
    }

    /// <summary>
    /// For each prefix and suffix group, its rows of its <see cref="BrickTierCount" /> weakest tiers (<see cref="BrickTiers" />),
    /// sorted weakest tier first, then by id, so a tier's rows sit together and its lowest id comes first.
    /// </summary>
    private static Dictionary<int, ItemModifier[]> BrickRows(IEnumerable<ItemModifier> accepted) => accepted
        .Where(m => m.Group.Kind is ItemModifierKind.Prefix or ItemModifierKind.Suffix)
        .GroupBy(m => m.GroupId)
        .ToDictionary(group => group.Key, group =>
        {
            HashSet<byte> weakest = [.. group.Select(m => m.Tier!.Value).Distinct().OrderDescending().Take(BrickTierCount)];
            return group.Where(m => weakest.Contains(m.Tier!.Value)).OrderByDescending(m => m.Tier).ThenBy(m => m.Id).ToArray();
        });

    /// <summary>Whether <paramref name="candidate" /> weighs above 0 on every base kind <paramref name="row" /> does.</summary>
    private static bool RollsOnEveryKindOf(ItemModifier candidate, ItemModifier row)
    {
        foreach (ItemModifierWeight weight in row.Weights)
        {
            if (weight.Weight > 0 && WeightOn(candidate, weight.BaseKind) <= 0)
                return false;
        }

        return true;
    }

    private static int WeightOn(ItemModifier modifier, ItemBaseKind kind)
    {
        foreach (ItemModifierWeight weight in modifier.Weights)
        {
            if (weight.BaseKind == kind)
                return weight.Weight;
        }

        return 0;
    }

    private static string? Refusal(ItemModifier modifier)
    {
        if (modifier.Group is null)
            return "it has no group";

        if (modifier.Stats.Count == 0)
            return "it has no stat line";

        bool tiered = modifier.Group.Kind is ItemModifierKind.Prefix or ItemModifierKind.Suffix or ItemModifierKind.Implicit;
        if (tiered != modifier.Tier is not null)
        {
            return tiered
                ? $"a {modifier.Group.Kind} row has no tier"
                : "a unique row has a tier, which only prefix, suffix and implicit rows have";
        }

        if (modifier.Group.Kind is ItemModifierKind.Unique or ItemModifierKind.Implicit && modifier.Weights.Count > 0)
            return $"a {modifier.Group.Kind} modifier carries a weight, but it only ever rolls through a template link";

        bool affix = modifier.Group.Kind is ItemModifierKind.Prefix or ItemModifierKind.Suffix;
        if (affix != modifier.Group.ThemeId is not null)
        {
            return affix
                ? $"a {modifier.Group.Kind} group has no theme"
                : $"a {modifier.Group.Kind} group has a theme, which only prefixes and suffixes carry";
        }

        return null;
    }

    private static string? LinkRefusal(ItemModifier?[] linked)
    {
        if (linked.Any(modifier => modifier is null))
            return "a link names a modifier the catalog does not hold";

        if (linked.Any(modifier => modifier!.Group.Kind is ItemModifierKind.Prefix or ItemModifierKind.Suffix))
            return "a link names a prefix or a suffix, which only ever roll by weight";

        if (linked.Any(modifier => modifier!.Group.Kind == ItemModifierKind.Unique) && linked.Length > 1)
            return "a unique modifier is linked beside another modifier; a unique template links its one unique modifier only";

        return null;
    }
}
