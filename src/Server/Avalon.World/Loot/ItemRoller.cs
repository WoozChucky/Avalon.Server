using System.Diagnostics;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Items;
using Avalon.World.Configuration;
using Avalon.World.Inventory;
using Avalon.World.Public.Enums;
using Avalon.World.Reload;
using Microsoft.Extensions.Options;

namespace Avalon.World.Loot;

/// <summary>
/// Makes copies of items with their base stats and modifiers (#1179). A copy rolls when it is made, on the tick, in
/// the purchase, use, reward, creation or kill that makes it; nothing rolls per tick or per hit.
/// </summary>
public interface IItemRoller
{
    /// <summary>
    /// A plain copy of <paramref name="template" /> at <paramref name="itemLevel" />: its template's rarity, its base stats
    /// (each uniform within its base's range, in ItemBaseStat order) and only the modifiers every copy of it rolls, ordinals
    /// from 0, each value uniform within its line's range: a base's implicit; a unique's base's base stats and implicit
    /// (<see cref="ItemsPatch.BaseOf" />), then its own modifier. A template with neither makes a copy with none.
    /// </summary>
    RolledItem RollPlain(ItemTemplate template, ushort itemLevel, ItemsPatch items);

    /// <summary>
    /// The full roll of a made copy, at the context's item level:
    /// <list type="bullet">
    /// <item>a Unique template rolls rarity Unique, its named base's base stats and implicit, then its one linked
    /// modifier;</item>
    /// <item>a Common template with a base kind becomes the best tier of its own line the item level reaches, rolls a
    /// rarity by the context's odds (Magic outright when it forces Magic), its base stats, its implicit, then its affixes
    /// by weight, and a rare its two-word name.</item>
    /// </list>
    /// Null for any other template, which does not roll, and for a Unique template the catalog left without its modifier.
    /// </summary>
    RolledItem? Roll(ItemTemplate template, in DropContext context, ItemsPatch items);

    /// <summary>
    /// Rolls a kill's or a container's drops in place: each drop whose template rolls carries its copy, names the copy's
    /// template and counts one item; gold and items that do not roll stay as they are, and a unique whose modifier
    /// the catalog left out is removed, so it never reaches the ground without its stats. A drop of a kind with several
    /// lines (a ring, an amulet) first draws one of them, uniformly, whichever line its entry names, and rolls as that
    /// line. After a Boss kill none of whose drops rolled Rare, the first drop that rolled a rarity is rolled again whole
    /// as Rare, as the tier it rolled; a unique is never touched.
    /// </summary>
    void RollDrops(List<RolledDrop> drops, in DropContext context, ItemsPatch items);

    /// <summary>
    /// Wakes <paramref name="sealedRow" />, a sealed affix of a copy at <paramref name="itemLevel" /> (#1180), over
    /// <paramref name="catalog" />: a new row with the same id, item and ordinal, no seal and a values list of its own; the
    /// sealed row is left as it was. A row the catalog no longer holds wakes as it was, with no draw. Otherwise one draw
    /// against Game:Items:Awakening:TierUpChancePct (none at 0 or 100) decides whether the wake rises: a miss wakes it as
    /// it was; a rise names the row's next better tier (<see cref="ItemModifierCatalog.NextTier" />) when that tier's
    /// required item level is within the copy's level plus <see cref="ItemAwakening.TierReach" />, its values rolled within
    /// that tier as a roll makes them, and otherwise polishes it: each line rolls again within its own tier, and the line
    /// whose ends sum higher is kept, the old one on a tie.
    /// </summary>
    AwakenedModifier Awaken(ItemInstanceModifier sealedRow, ushort itemLevel, ItemModifierCatalog catalog);

    /// <summary>
    /// Imprints <paramref name="glyphRow" />, a glyph's one row, onto <paramref name="targetRows" />, the rows of
    /// <paramref name="target" />, an item of <paramref name="kind" /> at <paramref name="itemLevel" /> holding
    /// <paramref name="instability" /> (#1180 part C), over <paramref name="catalog" />. The caller passes an instability
    /// below <see cref="ItemInstability.BrickAt" /> (a Tainted item is never imprinted), so the new total fits its ushort.
    /// A new list holds the target's rows in their order, then the glyph row's copy
    /// (<see cref="ItemInstanceModifier.CopyAs" />) under a new id, at the next ordinal after the highest, imprinted;
    /// neither the target's list nor any of its rows is written. Then one draw,
    /// uniform within Game:Items:Imprint's bounds, even when they are equal, is added to the total, which bricks the item at
    /// <see cref="ItemInstability.BrickAt" /> or more. Only then, each prefix and suffix in the list's order, the imprinted
    /// one included, rerolls into one of the rows <see cref="ItemModifierCatalog.BrickTiers" /> offers: its tier drawn
    /// among them (no draw when only one is offered), then its values as a roll makes them, as a new row keeping its id,
    /// item, ordinal, seal and imprinted mark. An implicit and a row the catalog no longer holds are kept, the same objects.
    /// A prefix or suffix offered no row throws (SeedIntegrityShould holds the seed to offering one), never kept at a tier
    /// outside the weakest three; it throws before anything is written, with no instability in its message.
    /// </summary>
    ImprintedRows Imprint(IReadOnlyList<ItemInstanceModifier> targetRows, ItemInstanceModifier glyphRow,
        ItemInstanceId target, ItemBaseKind kind, ushort itemLevel, ushort instability, ItemModifierCatalog catalog);
}

/// <summary>A sealed affix as it woke (#1180): its new row and how it woke.</summary>
public readonly record struct AwakenedModifier(ItemInstanceModifier Row, AwakeningOutcome Outcome);

/// <summary>
/// An imprint's outcome (#1180 part C): the item's new modifier list, its new instability total, and whether that total
/// bricked it. The total is World-side only: never sent, shown, logged or exported.
/// </summary>
public readonly record struct ImprintedRows(List<ItemInstanceModifier> Modifiers, ushort Instability, bool Bricked);

/// <summary>
/// <see cref="IItemRoller" /> over the loot random and the server's id allocator: the copy's id and each modifier's id
/// are handed out at the roll, so nothing is renamed when the copy is added. Stateless past its settings, so one serves
/// every thread. The draws come in one order, which the scripted tests follow: a drop's line (only a drop of a kind with
/// several lines), the rarity, the base stats in ItemBaseStat order, the implicit's values by line, the affix count, then
/// per affix one weighted pick and its values by line (the low end, then the high), then a rare's two words, and last a
/// rare's seal (#1180: only a rare with a prefix or suffix, and only at a Game:Items:SealedRarePct strictly between 0 and
/// 100). A unique draws its base's base stats and implicit, then its own lines' values. A wake (#1180) draws its tier-up
/// chance, then a rise's or a polish's values by line. An imprint (#1180 part C) draws its instability, and only when that
/// bricks the item, per prefix and suffix in ordinal order its tier (only when more than one is offered), then its values
/// by line.
/// </summary>
public sealed class ItemRoller(ILootRandom random, IItemIdAllocator itemIds, IOptions<GameConfiguration> options)
    : IItemRoller
{
    // The most affixes of one kind (prefix or suffix) a magic and a rare item carry; an imprint (#1180 part C) keeps to
    // them too (Items.ImprintRules).
    internal const int MagicRoom = 1, RareRoom = 3;

    // Above this many candidates on one kind, the pick's weights go on the heap rather than the stack. The seed's
    // largest kind has about a hundred.
    private const int StackCandidates = 256;

    // The modifiers of every copy that rolls none, shared: a store keeps no entry for none (ItemModifierStore.PutMade), so
    // nothing ever adds to it, and a plain copy of a template that links nothing allocates no list.
    private static readonly List<ItemInstanceModifier> s_none = [];

    private readonly CreatureRarityOdds _odds = options.Value.Items.Rarity;
    private readonly double _sealedRarePct = options.Value.Items.SealedRarePct;
    private readonly double _tierUpChancePct = options.Value.Items.Awakening.TierUpChancePct;
    private readonly int _instabilityMinPct = options.Value.Items.Imprint.InstabilityMinPct;
    private readonly int _instabilityMaxPct = options.Value.Items.Imprint.InstabilityMaxPct;

    public RolledItem RollPlain(ItemTemplate template, ushort itemLevel, ItemsPatch items)
    {
        ItemInstanceId instanceId = itemIds.Next();

        // A unique rolls the base it names first: its base stats and its implicit. Any other template is its own source.
        ItemTemplate source = items.BaseOf(template) ?? template;
        ItemBaseStats baseStats = RollBaseStats(source);
        IReadOnlyList<ItemModifier> inherited = ReferenceEquals(source, template) ? [] : items.Modifiers.LinkedTo(source.Id);
        IReadOnlyList<ItemModifier> links = items.Modifiers.LinkedTo(template.Id);
        if (inherited.Count + links.Count == 0)
            return new RolledItem(instanceId, template.Id, template.Rarity, itemLevel, RareName: null, baseStats, s_none);

        // The copy's own list, which the character's modifier store keeps as its entry (ItemModifierStore.PutMade).
        var modifiers = new List<ItemInstanceModifier>(inherited.Count + links.Count);
        RollLinks(modifiers, instanceId, inherited);
        RollLinks(modifiers, instanceId, links);
        return new RolledItem(instanceId, template.Id, template.Rarity, itemLevel, RareName: null, baseStats, modifiers);
    }

    public RolledItem? Roll(ItemTemplate template, in DropContext context, ItemsPatch items)
    {
        // A unique the catalog left without its own modifier does not roll; one with it is the plain copy at the level.
        if (template.Rarity == ItemRarity.Unique)
            return items.Modifiers.LinkedTo(template.Id).Count == 0 ? null : RollPlain(template, context.ItemLevel, items);

        if (!IsRolledBase(template))
            return null;

        ItemRarity rarity = context.ForceMagic ? ItemRarity.Magic : DrawRarity(context.Odds);
        return RollBase(TierAt(template, context.ItemLevel, items), template.BaseKind!.Value, rarity, context.ItemLevel,
            items.Modifiers, itemIds.Next());
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
                RolledItem? rolled = Roll(DrawLine(template, items), context, items);
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
    /// Common, Magic or Rare by the creature's odds: a draw of [0, 100) is Common below the common share, Magic
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
    /// Whether a copy of <paramref name="template" /> rolls as a base: a Common template with a base kind, whose
    /// copy takes the best tier of its line, its implicit, a rarity and affixes. The one test for it, which the quest
    /// rewards share (a base reward is rolled Magic).
    /// </summary>
    public static bool IsRolledBase(ItemTemplate template) =>
        template.Rarity == ItemRarity.Common && template.BaseKind is not null;

    /// <summary>
    /// The template a rolled copy of <paramref name="named" /> is made as at <paramref name="itemLevel" />: for a
    /// rolled base, the best template of its own line (<see cref="ItemsPatch.LineOf" />) whose required level the item
    /// level reaches, the lowest id among equals; <paramref name="named" /> when none does, and for any template that is
    /// not a rolled base. The line is sorted by required level, then id, so the first of the highest level reached wins.
    /// A named ring keeps its line: only a drop draws one (<see cref="RollDrops" />). What a quest reward's window shows and
    /// its turn-in checks are resolved by too, at the reward roll's item level.
    /// </summary>
    public static ItemTemplate TierAt(ItemTemplate named, ushort itemLevel, ItemsPatch items)
    {
        if (!IsRolledBase(named))
            return named;

        IReadOnlyList<ItemTemplate> line = items.LineOf(named);
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

    /// <summary>
    /// The template a drop of <paramref name="named" /> rolls as: for a rolled base of a kind with several lines (a ring,
    /// an amulet), the first tier of one of them, drawn uniformly, so its roll takes that line's best tier the item level
    /// reaches; <paramref name="named" /> for any other template, with no draw.
    /// </summary>
    private ItemTemplate DrawLine(ItemTemplate named, ItemsPatch items)
    {
        if (!IsRolledBase(named))
            return named;

        IReadOnlyList<IReadOnlyList<ItemTemplate>> lines = items.LinesOf(named.BaseKind!.Value);
        return lines.Count > 1 ? lines[(int)random.NextInt64(0, lines.Count)][0] : named;
    }

    /// <summary>
    /// A base's copy at <paramref name="rarity" />: its base stats, its implicit, then (Magic or Rare) its affixes, then a
    /// rare's name and, at Game:Items:SealedRarePct, the seal on its best affix (#1180, <see cref="SealIndex" />). The list
    /// is sized for the most the rarity can carry, so it never grows. A boss kill's re-roll as Rare comes here again, so it
    /// draws its own seal.
    /// </summary>
    private RolledItem RollBase(ItemTemplate template, ItemBaseKind kind, ItemRarity rarity, ushort itemLevel,
        ItemModifierCatalog catalog, ItemInstanceId instanceId)
    {
        ItemBaseStats baseStats = RollBaseStats(template);
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
            {
                rareName = RareNames.Draw(kind, random);
                if (modifiers.Count > links.Count && DrawSeal())
                    modifiers[SealIndex(modifiers, links.Count, catalog)].SealProgress = 0;
            }
        }

        return new RolledItem(instanceId, template.Id, rarity, itemLevel, rareName, baseStats, modifiers);
    }

    /// <summary>
    /// Whether a rare seals (#1180): never at a Game:Items:SealedRarePct of 0 and always at 100, neither with a draw, so
    /// those shares leave every seeded roll after it as it was; between them, one draw of [0, 100) below the share.
    /// </summary>
    private bool DrawSeal() =>
        _sealedRarePct > 0 && (_sealedRarePct >= 100 || random.NextDouble() * 100 < _sealedRarePct);

    /// <summary>
    /// The index of the affix a rare seals (#1180), among its prefixes and suffixes, from <paramref name="firstAffix" />
    /// on: the lowest tier number (the best tier); among equals, the highest roll of its first stat line within that
    /// line's range, <c>(value - min) / (max - min)</c>, or 1 for a range of one value, an "adds" line's two ends summed
    /// against its two ranges summed; among equals still, the lowest ordinal, which is the list's order. Rolls are compared
    /// by cross-multiplying in 64 bits, never in floating point, so equal rolls tie exactly. No draw, no allocation.
    /// </summary>
    private static int SealIndex(List<ItemInstanceModifier> modifiers, int firstAffix, ItemModifierCatalog catalog)
    {
        int best = -1;
        byte bestTier = 0;
        long bestRolled = 0, bestRange = 1;
        for (int i = firstAffix; i < modifiers.Count; i++)
        {
            ItemInstanceModifier row = modifiers[i];

            // The row was just picked from this catalog's candidates, every one a tiered prefix or suffix.
            ItemModifier modifier = catalog.Find(row.ModifierId)!;
            byte tier = modifier.Tier ?? byte.MaxValue;
            ItemModifierStat line = modifier.Stats[0];
            ItemInstanceModifierValue value = row.Values[0];
            long rolled = value.Value - (long)line.Min, range = line.Max - (long)line.Min;
            if (line.Min2 is { } min2 && line.Max2 is { } max2)
            {
                rolled += value.Value2.GetValueOrDefault() - (long)min2;
                range += max2 - (long)min2;
            }

            if (range <= 0)
                (rolled, range) = (1, 1);

            if (best < 0 || tier < bestTier || (tier == bestTier && rolled * bestRange > bestRolled * range))
                (best, bestTier, bestRolled, bestRange) = (i, tier, rolled, range);
        }

        return best;
    }

    /// <summary>
    /// The copy's base stats: each of its template's ranges (#1179), uniform in [Min, Max], drawn in ItemBaseStat order
    /// whatever order the rows were read in. None for a template without ranges. By value: nothing is allocated.
    /// </summary>
    private ItemBaseStats RollBaseStats(ItemTemplate template)
    {
        List<ItemTemplateBaseStat> ranges = template.BaseStats;
        ItemBaseStats rolled = default;
        if (ranges.Count == 0)
            return rolled;

        for (ItemBaseStat stat = ItemBaseStat.Armour; stat <= ItemBaseStat.AttackTime; stat++)
        {
            for (int i = 0; i < ranges.Count; i++)
            {
                ItemTemplateBaseStat range = ranges[i];
                if (range.Stat == stat)
                    rolled = rolled.With(stat, Uniform(range.Min, range.Max));
            }
        }

        return rolled;
    }

    private void RollLinks(List<ItemInstanceModifier> modifiers, ItemInstanceId instanceId, IReadOnlyList<ItemModifier> links)
    {
        for (int i = 0; i < links.Count; i++)
            modifiers.Add(Roll(instanceId, links[i], (byte)modifiers.Count));
    }

    /// <summary>
    /// Up to <paramref name="count" /> affixes, each a weighted pick among the candidates still eligible: within
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
    /// range also rolls its high end uniform in [Min2, Max2], raised to the low end when below it.
    /// </summary>
    private ItemInstanceModifier Roll(ItemInstanceId instanceId, ItemModifier modifier, byte ordinal)
    {
        Guid id = itemIds.NextModifierId();
        var rolled = new ItemInstanceModifier
        {
            Id = id,
            ItemInstanceId = instanceId,
            ModifierId = modifier.Id,
            Ordinal = ordinal,
        };

        // The row's own list, sized once: replacing it would throw away the one its initializer made.
        rolled.Values.Capacity = modifier.Stats.Count;
        RollValues(rolled, modifier);
        return rolled;
    }

    /// <summary>A value of each of <paramref name="modifier" />'s lines added to <paramref name="row" />, in the lines' order.</summary>
    private void RollValues(ItemInstanceModifier row, ItemModifier modifier)
    {
        foreach (ItemModifierStat line in modifier.Stats)
            row.Values.Add(RollValue(row.Id, line));
    }

    /// <summary>
    /// One value of <paramref name="line" />, uniform in [Min, Max]; a line with a second range also rolls its high end
    /// uniform in [Min2, Max2], raised to the low end when below it.
    /// </summary>
    private ItemInstanceModifierValue RollValue(Guid modifierId, ItemModifierStat line)
    {
        int value = Uniform(line.Min, line.Max);
        int? high = line.Min2 is { } min2 && line.Max2 is { } max2 ? Math.Max(Uniform(min2, max2), value) : null;
        return new ItemInstanceModifierValue
        {
            ItemInstanceModifierId = modifierId,
            Index = line.Index,
            Stat = line.Stat,
            Value = value,
            Value2 = high,
        };
    }

    public AwakenedModifier Awaken(ItemInstanceModifier sealedRow, ushort itemLevel, ItemModifierCatalog catalog)
    {
        // A row a reload left out has no tiers to rise through: it wakes as it was, and draws nothing.
        if (catalog.Find(sealedRow.ModifierId) is not { } modifier || !DrawTierUp())
            return new AwakenedModifier(Woken(sealedRow, sealedRow.ModifierId, Kept(sealedRow)), AwakeningOutcome.Woken);

        if (catalog.NextTier(modifier) is { } better && better.RequiredItemLevel <= itemLevel + ItemAwakening.TierReach)
        {
            ItemInstanceModifier raised = Woken(sealedRow, better.Id, new List<ItemInstanceModifierValue>(better.Stats.Count));
            RollValues(raised, better);
            return new AwakenedModifier(raised, AwakeningOutcome.TierRaised);
        }

        return new AwakenedModifier(Woken(sealedRow, sealedRow.ModifierId, Polished(sealedRow, modifier)),
            AwakeningOutcome.Polished);
    }

    /// <summary>
    /// Whether a wake rises (#1180): never at a Game:Items:Awakening:TierUpChancePct of 0 and always at 100, neither with a
    /// draw; between them, one draw of [0, 100) below the chance.
    /// </summary>
    private bool DrawTierUp() =>
        _tierUpChancePct > 0 && (_tierUpChancePct >= 100 || random.NextDouble() * 100 < _tierUpChancePct);

    /// <summary>
    /// The woken row: <paramref name="sealedRow" />'s id, item, ordinal and imprinted mark, as <paramref name="modifierId" />,
    /// unsealed.
    /// </summary>
    private static ItemInstanceModifier Woken(ItemInstanceModifier sealedRow, int modifierId,
        List<ItemInstanceModifierValue> values) => new()
        {
            Id = sealedRow.Id,
            ItemInstanceId = sealedRow.ItemInstanceId,
            ModifierId = modifierId,
            Ordinal = sealedRow.Ordinal,
            Values = values,
            SealProgress = null,
            Imprinted = sealedRow.Imprinted,
        };

    /// <summary>A copy of each of the row's values, in its order.</summary>
    private static List<ItemInstanceModifierValue> Kept(ItemInstanceModifier row)
    {
        var values = new List<ItemInstanceModifierValue>(row.Values.Count);
        foreach (ItemInstanceModifierValue value in row.Values)
            values.Add(value.Copy());

        return values;
    }

    /// <summary>
    /// The polish: each of the row's values, in index order, rolled again within its line of <paramref name="modifier" />
    /// as a roll makes it, the new one kept only when its ends sum higher than the old; a value whose line the catalog no
    /// longer has is kept. Each kept line is whole, so its high end stays at or above its low end.
    /// </summary>
    private List<ItemInstanceModifierValue> Polished(ItemInstanceModifier row, ItemModifier modifier)
    {
        var values = new List<ItemInstanceModifierValue>(row.Values.Count);
        foreach (ItemInstanceModifierValue old in row.Values)
        {
            ItemModifierStat? line = LineAt(modifier, old.Index);
            if (line is null)
            {
                values.Add(old.Copy());
                continue;
            }

            ItemInstanceModifierValue rolled = RollValue(row.Id, line);
            long before = old.Value + (long)old.Value2.GetValueOrDefault();
            long after = rolled.Value + (long)rolled.Value2.GetValueOrDefault();
            values.Add(after > before ? rolled : old.Copy());
        }

        return values;
    }

    public ImprintedRows Imprint(IReadOnlyList<ItemInstanceModifier> targetRows, ItemInstanceModifier glyphRow,
        ItemInstanceId target, ItemBaseKind kind, ushort itemLevel, ushort instability, ItemModifierCatalog catalog)
    {
        var modifiers = new List<ItemInstanceModifier>(targetRows.Count + 1);
        int highest = -1;
        for (int i = 0; i < targetRows.Count; i++)
        {
            modifiers.Add(targetRows[i]);
            highest = Math.Max(highest, targetRows[i].Ordinal);
        }

        modifiers.Add(glyphRow.CopyAs(itemIds.NextModifierId(), target, (byte)(highest + 1), imprinted: true));

        // Below BrickAt plus a draw of at most 100, the total always fits the ushort it is returned as.
        Debug.Assert(instability < ItemInstability.BrickAt, "A Tainted item is never imprinted.");
        int total = instability + Uniform(_instabilityMinPct, _instabilityMaxPct);
        bool bricked = total >= ItemInstability.BrickAt;
        if (bricked)
        {
            Span<int> offered = stackalloc int[ItemModifierCatalog.BrickTierCount];
            for (int i = 0; i < modifiers.Count; i++)
                modifiers[i] = Bricked(modifiers[i], kind, itemLevel, catalog, offered);
        }

        return new ImprintedRows(modifiers, (ushort)total, bricked);
    }

    /// <summary>
    /// <paramref name="row" /> as a brick leaves it: a prefix or suffix offered a row by
    /// <see cref="ItemModifierCatalog.BrickTiers" /> becomes a new row of the offered tier it draws (no draw when only one
    /// is offered), its values rolled within that tier, keeping its id, item, ordinal, seal and imprinted mark; any other
    /// row is returned as it was. A prefix or suffix offered none throws: a brick never keeps a tier it cannot offer.
    /// </summary>
    private ItemInstanceModifier Bricked(ItemInstanceModifier row, ItemBaseKind kind, ushort itemLevel,
        ItemModifierCatalog catalog, Span<int> offered)
    {
        if (catalog.Find(row.ModifierId) is not { Group.Kind: ItemModifierKind.Prefix or ItemModifierKind.Suffix } modifier)
            return row;

        int n = catalog.BrickTiers(modifier, kind, itemLevel, offered);
        if (n == 0)
        {
            throw new InvalidOperationException(
                $"Modifier {modifier.Id} is offered no brick tier on {kind} at item level {itemLevel}.");
        }

        // The offered ids are rows of this catalog, so each is found.
        ItemModifier picked = catalog.Find(offered[n == 1 ? 0 : (int)random.NextInt64(0, n)])!;
        var rerolled = new ItemInstanceModifier
        {
            Id = row.Id,
            ItemInstanceId = row.ItemInstanceId,
            ModifierId = picked.Id,
            Ordinal = row.Ordinal,
            Values = new List<ItemInstanceModifierValue>(picked.Stats.Count),
            SealProgress = row.SealProgress,
            Imprinted = row.Imprinted,
        };
        RollValues(rerolled, picked);
        return rerolled;
    }

    private static ItemModifierStat? LineAt(ItemModifier modifier, byte index)
    {
        foreach (ItemModifierStat line in modifier.Stats)
        {
            if (line.Index == index)
                return line;
        }

        return null;
    }

    private int Uniform(int min, int max) => (int)random.NextInt64(min, (long)max + 1);
}
