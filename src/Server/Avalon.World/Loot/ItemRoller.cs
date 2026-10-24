using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Configuration;
using Avalon.World.Inventory;
using Avalon.World.Public.Enums;
using Avalon.World.Reload;
using Microsoft.Extensions.Options;

namespace Avalon.World.Loot;

/// <summary>
/// Makes copies of items with their modifiers (#1179). A copy rolls when it is made, on the tick, in the purchase, use,
/// reward, creation or kill that makes it; nothing rolls per tick or per hit.
/// </summary>
public interface IItemRoller
{
    /// <summary>
    /// A plain copy of <paramref name="template" /> at <paramref name="itemLevel" />: its template's rarity and only the
    /// modifiers every copy of it rolls (B-R12), one per link of <paramref name="catalog" />, ordinals from 0 in link order,
    /// each value uniform within its line's range. A template that links nothing makes a copy with none.
    /// </summary>
    RolledItem RollPlain(ItemTemplate template, ushort itemLevel, ItemModifierCatalog catalog);

    /// <summary>
    /// The full roll of a made copy (B-R14 to B-R19), at the context's item level:
    /// <list type="bullet">
    /// <item>a Unique template rolls rarity Unique and its one linked modifier;</item>
    /// <item>a Common template with a base kind becomes the best tier of its line the item level reaches, rolls a rarity
    /// by the context's odds (Magic outright when it forces Magic), its implicits, then its affixes by weight, and a rare
    /// its two-word name.</item>
    /// </list>
    /// Null for any other template, which does not roll, and for a Unique template the catalog left without its modifier.
    /// </summary>
    RolledItem? Roll(ItemTemplate template, in DropContext context, ItemsPatch items);

    /// <summary>
    /// Rolls a kill's or a container's drops in place: each drop whose template rolls carries its copy, names the copy's
    /// template and counts one item (B-R18); gold and items that do not roll stay as they are, and a unique whose modifier
    /// the catalog left out is removed, so it never reaches the ground without its stats. After a Boss kill none of whose
    /// drops rolled Rare, the first drop that rolled a rarity is rolled again whole as Rare (B-R20); a unique is never
    /// touched.
    /// </summary>
    void RollDrops(List<RolledDrop> drops, in DropContext context, ItemsPatch items);
}

/// <summary>
/// <see cref="IItemRoller" /> over the loot random and the server's id allocator: the copy's id and each modifier's id
/// are handed out at the roll, so nothing is renamed when the copy is added. Stateless past its settings, so one serves
/// every thread. The draws come in one order, which the scripted tests follow: the rarity, the implicits' values by link
/// and line, the affix count, then per affix one weighted pick and its values by line (the low end, then the high), and
/// last a rare's two words. A unique draws only its lines' values.
/// </summary>
public sealed class ItemRoller(ILootRandom random, IItemIdAllocator itemIds, IOptions<GameConfiguration> options)
    : IItemRoller
{
    // The most affixes of one kind (prefix or suffix) a magic and a rare item carry.
    private const int MagicRoom = 1, RareRoom = 3;

    // Above this many candidates on one kind, the pick's weights go on the heap rather than the stack. The seed's
    // largest kind has about a hundred.
    private const int StackCandidates = 256;

    private readonly CreatureRarityOdds _odds = options.Value.Items.Rarity;

    public RolledItem RollPlain(ItemTemplate template, ushort itemLevel, ItemModifierCatalog catalog)
    {
        ItemInstanceId instanceId = itemIds.Next();
        IReadOnlyList<ItemModifier> links = catalog.LinkedTo(template.Id);
        if (links.Count == 0)
            return new RolledItem(instanceId, template.Id, template.Rarity, itemLevel, RareName: null, []);

        // The copy's own list, which the character's modifier store keeps as its entry (ItemModifierStore.PutMade).
        var modifiers = new List<ItemInstanceModifier>(links.Count);
        RollLinks(modifiers, instanceId, links);
        return new RolledItem(instanceId, template.Id, template.Rarity, itemLevel, RareName: null, modifiers);
    }

    public RolledItem? Roll(ItemTemplate template, in DropContext context, ItemsPatch items)
    {
        if (template.Rarity == ItemRarity.Unique)
            return RollUnique(template, context.ItemLevel, items.Modifiers);

        if (template.Rarity != ItemRarity.Common || template.BaseKind is not { } kind)
            return null;

        ItemRarity rarity = context.ForceMagic ? ItemRarity.Magic : DrawRarity(context.Odds);
        return RollBase(BestTier(template, kind, context.ItemLevel, items), kind, rarity, context.ItemLevel, items.Modifiers,
            itemIds.Next());
    }

    public void RollDrops(List<RolledDrop> drops, in DropContext context, ItemsPatch items)
    {
        int kept = 0, firstRarity = -1;
        bool anyRare = false;
        for (int i = 0; i < drops.Count; i++)
        {
            RolledDrop drop = drops[i];
            if (drop.ItemTemplateId is { } id && items.Find(id) is { } template)
            {
                RolledItem? rolled = Roll(template, context, items);
                if (rolled is null && template.Rarity == ItemRarity.Unique)
                    continue;

                if (rolled is not null)
                {
                    drop = drop with { ItemTemplateId = rolled.TemplateId, Count = 1, Rolled = rolled };
                    anyRare |= rolled.Rarity == ItemRarity.Rare;
                    if (firstRarity < 0 && rolled.Rarity != ItemRarity.Unique)
                        firstRarity = kept;
                }
            }

            drops[kept++] = drop;
        }

        if (kept < drops.Count)
            drops.RemoveRange(kept, drops.Count - kept);

        if (context.Odds != CreatureRarity.Boss || anyRare || firstRarity < 0)
            return;

        // The boss guarantee: the first drop that rolled a rarity rolls again whole, as Rare, under the id it already had.
        RolledDrop upgraded = drops[firstRarity];
        RolledItem previous = upgraded.Rolled!;
        ItemTemplate tier = items.Find(previous.TemplateId)!;
        drops[firstRarity] = upgraded with
        {
            Rolled = RollBase(tier, tier.BaseKind!.Value, ItemRarity.Rare, context.ItemLevel, items.Modifiers,
                previous.InstanceId),
        };
    }

    /// <summary>
    /// Common, Magic or Rare by the creature's odds (B-R14): a draw of [0, 100) is Common below the common share, Magic
    /// below the common and magic shares together, and Rare above.
    /// </summary>
    private ItemRarity DrawRarity(CreatureRarity creature)
    {
        RarityOdds odds = _odds.For(creature);
        double draw = random.NextDouble() * 100;
        if (draw < 100 - odds.MagicPct - odds.RarePct)
            return ItemRarity.Common;

        return draw < 100 - odds.RarePct ? ItemRarity.Magic : ItemRarity.Rare;
    }

    /// <summary>
    /// The best Common template of <paramref name="kind" />'s line whose required level the item level reaches, the
    /// lowest id among equals (B-R16); <paramref name="named" /> when none does. The line is sorted by required level,
    /// then id, so the first of the highest level reached wins.
    /// </summary>
    private static ItemTemplate BestTier(ItemTemplate named, ItemBaseKind kind, ushort itemLevel, ItemsPatch items)
    {
        IReadOnlyList<ItemTemplate> line = items.LineOf(kind);
        ItemTemplate? best = null;
        for (int i = 0; i < line.Count; i++)
        {
            ItemTemplate tier = line[i];
            ushort required = tier.RequiredLevel ?? 1;
            if (required > itemLevel)
                break;

            if (best is null || required > (best.RequiredLevel ?? 1))
                best = tier;
        }

        return best ?? named;
    }

    private RolledItem? RollUnique(ItemTemplate template, ushort itemLevel, ItemModifierCatalog catalog)
    {
        IReadOnlyList<ItemModifier> links = catalog.LinkedTo(template.Id);
        if (links.Count == 0)
            return null;

        ItemInstanceId instanceId = itemIds.Next();
        var modifiers = new List<ItemInstanceModifier>(links.Count);
        RollLinks(modifiers, instanceId, links);
        return new RolledItem(instanceId, template.Id, ItemRarity.Unique, itemLevel, RareName: null, modifiers);
    }

    /// <summary>
    /// A base's copy at <paramref name="rarity" />: its implicits, then (Magic or Rare) its affixes, then a rare's name.
    /// The list is sized for the most the rarity can carry, so it never grows.
    /// </summary>
    private RolledItem RollBase(ItemTemplate template, ItemBaseKind kind, ItemRarity rarity, ushort itemLevel,
        ItemModifierCatalog catalog, ItemInstanceId instanceId)
    {
        IReadOnlyList<ItemModifier> links = catalog.LinkedTo(template.Id);
        int room = rarity switch
        {
            ItemRarity.Magic => MagicRoom,
            ItemRarity.Rare => RareRoom,
            _ => 0,
        };
        var modifiers = new List<ItemInstanceModifier>(links.Count + 2 * room);
        RollLinks(modifiers, instanceId, links);

        string? rareName = null;
        if (room > 0)
        {
            int count = rarity == ItemRarity.Magic ? (int)random.NextInt64(1, 3) : (int)random.NextInt64(3, 7);
            RollAffixes(modifiers, instanceId, catalog.CandidatesFor(kind), count, room, itemLevel);
            if (rarity == ItemRarity.Rare)
                rareName = RareNames.Draw(kind, random);
        }

        return new RolledItem(instanceId, template.Id, rarity, itemLevel, rareName, modifiers);
    }

    private void RollLinks(List<ItemInstanceModifier> modifiers, ItemInstanceId instanceId, IReadOnlyList<ItemModifier> links)
    {
        for (int i = 0; i < links.Count; i++)
            modifiers.Add(Roll(instanceId, links[i], (byte)modifiers.Count));
    }

    /// <summary>
    /// Up to <paramref name="count" /> affixes (B-R17), each a weighted pick among the candidates still eligible: within
    /// the item level, of a kind with room left and of a group the item does not carry yet. Stops early when none is.
    /// </summary>
    private void RollAffixes(List<ItemInstanceModifier> modifiers, ItemInstanceId instanceId,
        IReadOnlyList<ItemModifierCandidate> candidates, int count, int room, ushort itemLevel)
    {
        int n = candidates.Count;
        Span<int> weights = n <= StackCandidates ? stackalloc int[n] : new int[n];
        Span<int> groups = stackalloc int[2 * RareRoom];
        int taken = 0, prefixes = 0, suffixes = 0;

        for (int draw = 0; draw < count; draw++)
        {
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                ItemModifierCandidate candidate = candidates[i];
                ItemModifier modifier = candidate.Modifier;
                bool prefix = modifier.Group.Kind == ItemModifierKind.Prefix;
                bool eligible = modifier.RequiredItemLevel <= itemLevel
                                && (prefix ? prefixes : suffixes) < room
                                && !groups[..taken].Contains(modifier.GroupId);
                weights[i] = eligible ? candidate.Weight : 0;
                any |= eligible;
            }

            if (!any)
                return;

            ItemModifier picked = candidates[WeightedPick.Index(weights, random.NextDouble())].Modifier;
            groups[taken++] = picked.GroupId;
            if (picked.Group.Kind == ItemModifierKind.Prefix)
                prefixes++;
            else
                suffixes++;

            modifiers.Add(Roll(instanceId, picked, (byte)modifiers.Count));
        }
    }

    /// <summary>
    /// One modifier on the copy: a value per stat line, in the line's index, uniform in [Min, Max]; a line with a second
    /// range also rolls its high end uniform in [Min2, Max2], raised to the low end when below it (B-R15).
    /// </summary>
    private ItemInstanceModifier Roll(ItemInstanceId instanceId, ItemModifier modifier, byte ordinal)
    {
        Guid id = itemIds.NextModifierId();
        var values = new List<ItemInstanceModifierValue>(modifier.Stats.Count);
        foreach (ItemModifierStat line in modifier.Stats)
        {
            int value = Uniform(line.Min, line.Max);
            int? high = line.Min2 is { } min2 && line.Max2 is { } max2 ? Math.Max(Uniform(min2, max2), value) : null;
            values.Add(new ItemInstanceModifierValue
            {
                ItemInstanceModifierId = id,
                Index = line.Index,
                Stat = line.Stat,
                Value = value,
                Value2 = high,
            });
        }

        return new ItemInstanceModifier
        {
            Id = id,
            ItemInstanceId = instanceId,
            ModifierId = modifier.Id,
            Ordinal = ordinal,
            Values = values,
        };
    }

    private int Uniform(int min, int max) => (int)random.NextInt64(min, (long)max + 1);
}
