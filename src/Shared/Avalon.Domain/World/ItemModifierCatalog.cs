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
/// <item>a modifier with no group or no stat line, or a unique or implicit modifier that carries a weight;</item>
/// <item>a template's links when one names a modifier left out or a prefix or suffix, or when they mix a unique
/// modifier with any other link (a unique template links its one Unique modifier, a common one only Implicits). The
/// template is then left with no links at all, never with part of them.</item>
/// </list>
/// Which template is unique is the template's own fact, held against these links by the seed's guard test.
/// </summary>
public sealed class ItemModifierCatalog
{
    private static readonly int s_kinds = Enum.GetValues<ItemBaseKind>().Max(kind => (int)kind) + 1;

    private readonly Dictionary<int, ItemModifier> _modifiers;
    private readonly Dictionary<ulong, ItemModifier[]> _links;
    private readonly ItemModifierCandidate[][] _candidates;

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

    /// <summary>How many modifiers the catalog accepted.</summary>
    public int Count => _modifiers.Count;

    /// <summary>The accepted modifier of that id, with its group, stat lines and weights; null when there is none.</summary>
    public ItemModifier? Find(int modifierId) => _modifiers.GetValueOrDefault(modifierId);

    /// <summary>The modifiers every copy of the template rolls, in link order; empty when it links none.</summary>
    public IReadOnlyList<ItemModifier> LinkedTo(ItemTemplateId template) =>
        template is not null && _links.TryGetValue(template.Value, out ItemModifier[]? linked) ? linked : [];

    /// <summary>The prefix and suffix rows weighing above 0 on <paramref name="kind" />, by id; empty for none.</summary>
    public IReadOnlyList<ItemModifierCandidate> CandidatesFor(ItemBaseKind kind) =>
        (int)kind >= 0 && (int)kind < _candidates.Length ? _candidates[(int)kind] : [];

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

        if (modifier.Group.Kind is ItemModifierKind.Unique or ItemModifierKind.Implicit && modifier.Weights.Count > 0)
            return $"a {modifier.Group.Kind} modifier carries a weight, but it only ever rolls through a template link";

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
