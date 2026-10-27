using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Abilities;
using Avalon.World.Auras;
using Avalon.World.Dialogue;
using Avalon.World.Doodads;
using Avalon.World.Items;
using Avalon.World.Loot;
using Avalon.World.Public.Dialogue;
using Avalon.World.Public.Localization;
using Avalon.World.Talents;
using Avalon.World.Vendors;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Reload;

/// <summary>
/// Everything one area assigns, built entirely before anything is applied. An area is never
/// half-reloaded: prepare either produces a whole patch or throws.
/// </summary>
public abstract record StaticDataPatch(ReloadArea Area)
{
    /// <summary>What changed, for the game master's reply — e.g. "14 texts, 6 nodes, 9 options".</summary>
    public abstract string Describe();
}

public sealed record DialoguePatch(
    ILocalizedTextCatalog Texts,
    IDialogueCatalog Dialogue,
    int TextCount,
    int NodeCount,
    int OptionCount,
    DialogueActions Actions)
    : StaticDataPatch(ReloadArea.Dialogue)
{
    public override string Describe() => $"{TextCount} texts, {NodeCount} nodes, {OptionCount} options";
}

public sealed record CreaturesPatch(
    IReadOnlyCollection<CreatureTemplate> Templates,
    IReadOnlyCollection<CreatureBaseStat> BaseStats,
    IReadOnlyCollection<CreatureRarityModifier> Rarities,
    CreatureStatDeriver Stats)
    : StaticDataPatch(ReloadArea.Creatures)
{
    public override string Describe()
        => $"{Templates.Count} templates, {BaseStats.Count} base stats, {Rarities.Count} rarities";

    /// <summary>
    /// #627, #770 and #1145: see <see cref="CreatureTemplateRules.Validate" /> and
    /// <see cref="CreatureTemplateRules.ValidateRarities" />.
    /// </summary>
    public static void Validate(IEnumerable<CreatureTemplate> templates, IEnumerable<CreatureRarityModifier> rarities)
    {
        CreatureTemplateRules.Validate(templates);
        CreatureTemplateRules.ValidateRarities(rarities);
    }
}

/// <summary>
/// Ability templates, validated (#164), and the talent tree checked against them (#1181), one generation. Read when a
/// character is selected, so a reload reaches the next select; abilities a character already holds keep the metadata
/// they were built with, and a character online keeps its abilities until its next allocation or select.
/// </summary>
public sealed record AbilitiesPatch(AbilityCatalog Catalog, TalentCatalog Talents) : StaticDataPatch(ReloadArea.Abilities)
{
    public override string Describe() => $"{Catalog.Describe()}, {Talents.Describe()}";
}

/// <summary>
/// The item templates and the modifier catalog (#1179), one generation: a /reload items reloads both, so a template and
/// the modifiers it links are never from two reads. The catalog is not live-editable (balance tables, migrations only).
/// </summary>
public sealed record ItemsPatch(IReadOnlyCollection<ItemTemplate> Templates, ItemModifierCatalog Modifiers)
    : StaticDataPatch(ReloadArea.Items)
{
    private static readonly int s_kinds = Enum.GetValues<ItemBaseKind>().Max(kind => (int)kind) + 1;

    // The last generation stamped, process-wide; the first patch is 1, so 0 stands for none.
    private static long s_lastGeneration;

    private readonly KeyedRows<ulong, ItemTemplate> _templates = Keyed(Templates);
    private readonly ItemModifierCatalog _modifiers = Modifiers;
    private readonly BaseLines _lines = BaseLines.Of(Templates, Modifiers);
    private readonly ItemTemplate? _glyph = GlyphOf(Templates);

    /// <summary>
    /// This patch's stamp (#1179), unique in the process and never 0: what a name worked out from it is keyed by
    /// (DescribedName), so a copy compares a number, not a reference that would keep an old generation's templates and
    /// catalog alive after a reload. Stamped when the patch is built, and again by a <c>with</c> that changes either part.
    /// </summary>
    public long Generation { get; private init; } = NextGeneration();

    /// <summary>
    /// The modifier catalog of this generation, which also draws the bases' lines; replacing it rebuilds them and stamps
    /// a new generation.
    /// </summary>
    public ItemModifierCatalog Modifiers
    {
        get => _modifiers;
        init
        {
            _modifiers = value;
            _lines = BaseLines.Of(_templates, value);
            Generation = NextGeneration();
        }
    }

    /// <summary>
    /// The templates, with their lookup by id (<see cref="KeyedRows{TKey,TRow}" />, #883) and their lines: replaced with
    /// the patch, so a reload cannot leave a lookup behind.
    /// </summary>
    public IReadOnlyCollection<ItemTemplate> Templates
    {
        get => _templates;
        init
        {
            _templates = Keyed(value);
            _lines = BaseLines.Of(value, _modifiers);
            _glyph = GlyphOf(value);
            Generation = NextGeneration();
        }
    }

    /// <summary>
    /// The Resonance effects of this generation (#1180 part A2), keyed by the themes of <see cref="Modifiers" />; an Auras
    /// reload replaces them with the same rows held against its auras (<see cref="AurasPatch.Effects" />). Not part of
    /// <see cref="Generation" />: no name is worked out from them.
    /// </summary>
    public ResonanceEffectCatalog Effects { get; init; } = ResonanceEffectCatalog.Empty;

    /// <summary>
    /// The template every glyph is a copy of (#1180 part C): the lowest-id template whose subclass is
    /// <see cref="ItemSubClass.Glyph" />, or null when the patch holds none. Worked out when the templates are set; nothing
    /// finds it by its id.
    /// </summary>
    public ItemTemplate? Glyph => _glyph;

    /// <summary>The template of that id, or null: by key, where a reader walked the templates.</summary>
    public ItemTemplate? Find(ItemTemplateId id) => id is null ? null : _templates.Find(id.Value);

    /// <summary>
    /// The patch, with each template held to three rules (#1179). Its links against its rarity and kind (the half of the
    /// link rules the catalog cannot see, ItemModifierCatalog.RarityRefusal): a Unique template links exactly one Unique
    /// modifier, a Common base exactly one Implicit, and no other template links any. Its own base-stat rows against its
    /// kind (<see cref="ItemBaseStatRules.Problem" />): exactly the stats its kind carries, each range sound, and a damage
    /// type exactly on a weapon. And a Unique template's named base (<see cref="ItemBaseStatRules.NamedBaseProblem" />,
    /// which the API's template edit shares): a Common base of its
    /// kind, at or under its level, that is itself kept; no other template names one. A template that breaks any is
    /// logged and left out whole, with its links, so the world never makes a copy of it, rather than kept with its links
    /// or base stats stripped (a stat-less unique, a unique standing in its line as a base, a chest without armour, a base
    /// without its implicit). A Unique template that links nothing is kept: one whose modifier the catalog refused has its
    /// drops removed by the roll. A patch kept with no Glyph template, or more than one, is logged at Error (#1180 part C;
    /// <see cref="Glyph" />). The Resonance effects (#1180 part A2) are held to their rules against
    /// <paramref name="auras" />, the aura rows read in the same prepare (<see cref="ResonanceEffectCatalog" />).
    /// </summary>
    public static ItemsPatch Build(IReadOnlyCollection<ItemTemplate> templates, ItemModifierCatalog modifiers,
        IReadOnlyCollection<ItemThemeEffect> effects, AuraCatalog auras, ILogger logger)
    {
        HashSet<ulong> refused = [];
        void Judge(ItemTemplate template, string? reason)
        {
            if (reason is null)
                return;

            logger.LogError("Item template {TemplateId} is left out: {Reason}", template.Id.Value, reason);
            refused.Add(template.Id.Value);
        }

        // The bases and every other non-unique first, so each unique is judged against the bases that stay.
        foreach (ItemTemplate template in templates.Where(t => t.Rarity != ItemRarity.Unique))
        {
            Judge(template, ItemModifierCatalog.RarityRefusal(template.Rarity, template.BaseKind, modifiers.LinkedTo(template.Id))
                            ?? ItemBaseStatRules.Problem(template)
                            ?? ItemBaseStatRules.NamedBaseProblem(template, null));
        }

        var kept = templates
            .Where(t => t.Rarity != ItemRarity.Unique && !refused.Contains(t.Id.Value))
            .ToDictionary(t => t.Id.Value);
        foreach (ItemTemplate unique in templates.Where(t => t.Rarity == ItemRarity.Unique))
        {
            IReadOnlyList<ItemModifier> linked = modifiers.LinkedTo(unique.Id);
            ItemTemplate? named = unique.BaseTemplateId is { } id ? kept.GetValueOrDefault(id.Value) : null;
            Judge(unique, (linked.Count == 0 ? null : ItemModifierCatalog.RarityRefusal(unique.Rarity, unique.BaseKind, linked))
                          ?? ItemBaseStatRules.Problem(unique)
                          ?? ItemBaseStatRules.NamedBaseProblem(unique, named));
        }

        // Extraction makes its glyphs of the one Glyph template (#1180 part C): without it the Workbench extracts nothing,
        // and with more than one the lowest id stands. Counted as Glyph finds them: a template without an id is none.
        int glyphs = templates.Count(t => t is { SubClass: ItemSubClass.Glyph, Id: { } id } && !refused.Contains(id.Value));
        if (glyphs != 1)
        {
            logger.LogError("The item templates hold {Count} Glyph templates, not one: {Outcome}", glyphs,
                glyphs == 0 ? "nothing can be extracted" : "the lowest id is used");
        }

        var catalog = new ResonanceEffectCatalog(effects, auras, logger);
        return refused.Count == 0
            ? new ItemsPatch(templates, modifiers) { Effects = catalog }
            : new ItemsPatch([.. templates.Where(t => !refused.Contains(t.Id.Value))], modifiers.WithoutLinksOf(refused))
            {
                Effects = catalog,
            };
    }

    /// <summary>
    /// The base a copy of <paramref name="template" /> rolls its base stats and implicit from (#1179): the template itself
    /// for a Common base, its named base for a Unique template, and null for any other template (a consumable, a quest
    /// item) or a unique whose base this patch does not hold.
    /// </summary>
    public ItemTemplate? BaseOf(ItemTemplate template) => template switch
    {
        { Rarity: ItemRarity.Common, BaseKind: not null } => template,
        { Rarity: ItemRarity.Unique, BaseTemplateId: { } named } => Find(named),
        _ => null,
    };

    /// <summary>
    /// The line <paramref name="named" /> belongs to (#1179): the Common templates of its kind that link the same implicit
    /// group, the tiers, by required level and then id; empty for a template that is no Common base of this patch. Built
    /// once per patch.
    /// </summary>
    public IReadOnlyList<ItemTemplate> LineOf(ItemTemplate named) => _lines.Of(named);

    /// <summary>The lines of <paramref name="kind" />, by each line's first id; empty for a kind with none.</summary>
    public IReadOnlyList<IReadOnlyList<ItemTemplate>> LinesOf(ItemBaseKind kind) => _lines.Of(kind);

    private static long NextGeneration() => Interlocked.Increment(ref s_lastGeneration);

    private static ItemTemplate? GlyphOf(IReadOnlyCollection<ItemTemplate> templates) =>
        templates.Where(template => template is { SubClass: ItemSubClass.Glyph, Id: not null })
            .MinBy(template => template.Id.Value);

    private static KeyedRows<ulong, ItemTemplate> Keyed(IReadOnlyCollection<ItemTemplate> templates) =>
        KeyedRows<ulong, ItemTemplate>.Over(templates, static template => template.Id?.Value);

    /// <summary>
    /// The bases' lines (#1179): per kind, the Common templates that link the same implicit group (by its id, or none),
    /// each line sorted by required level then id, the kind's lines by their first id; and each base's own line by id.
    /// </summary>
    private sealed class BaseLines
    {
        private readonly IReadOnlyList<ItemTemplate>[][] _byKind;
        private readonly Dictionary<ulong, IReadOnlyList<ItemTemplate>> _byTemplate;

        private BaseLines(IReadOnlyList<ItemTemplate>[][] byKind, Dictionary<ulong, IReadOnlyList<ItemTemplate>> byTemplate)
        {
            _byKind = byKind;
            _byTemplate = byTemplate;
        }

        public static BaseLines Of(IReadOnlyCollection<ItemTemplate> templates, ItemModifierCatalog modifiers)
        {
            var byKind = new IReadOnlyList<ItemTemplate>[s_kinds][];
            Dictionary<ulong, IReadOnlyList<ItemTemplate>> byTemplate = [];
            for (int kind = 0; kind < s_kinds; kind++)
            {
                byKind[kind] =
                [
                    .. templates
                        .Where(template => template.Rarity == ItemRarity.Common && template.BaseKind == (ItemBaseKind)kind)
                        .GroupBy(template => modifiers.LinkedTo(template.Id) is [{ } linked, ..] ? linked.GroupId : 0)
                        .Select(line => (IReadOnlyList<ItemTemplate>)line
                            .OrderBy(template => template.RequiredLevel ?? 1).ThenBy(template => template.Id.Value).ToArray())
                        .OrderBy(line => line[0].Id.Value),
                ];
                foreach (IReadOnlyList<ItemTemplate> line in byKind[kind])
                {
                    foreach (ItemTemplate template in line)
                        byTemplate[template.Id.Value] = line;
                }
            }

            return new BaseLines(byKind, byTemplate);
        }

        public IReadOnlyList<ItemTemplate> Of(ItemTemplate named) =>
            named.Id is not null && _byTemplate.TryGetValue(named.Id.Value, out IReadOnlyList<ItemTemplate>? line) ? line : [];

        public IReadOnlyList<IReadOnlyList<ItemTemplate>> Of(ItemBaseKind kind) =>
            (int)kind >= 0 && (int)kind < _byKind.Length ? _byKind[(int)kind] : [];
    }

    public override string Describe() =>
        $"{Templates.Count} item templates, {Modifiers.Count} item modifiers, {Effects.Effects.Count} Resonance effects";
}

public sealed record ProgressionPatch(
    IReadOnlyCollection<CharacterLevelExperience> Levels,
    IReadOnlyCollection<ClassLevelStat> ClassStats,
    IReadOnlyCollection<CharacterCreateInfo> CreateInfos)
    : StaticDataPatch(ReloadArea.Progression)
{
    private readonly KeyedRows<ushort, CharacterLevelExperience> _levels = Keyed(Levels);

    /// <summary>The experience rows, with their lookup by level (<see cref="KeyedRows{TKey,TRow}" />, #883), as the item templates have theirs.</summary>
    public IReadOnlyCollection<CharacterLevelExperience> Levels
    {
        get => _levels;
        init => _levels = Keyed(value);
    }

    /// <summary>The experience row of that level, or null: by key, where a reader walked the rows.</summary>
    public CharacterLevelExperience? FindLevel(ushort level) => _levels.Find(level);

    private static KeyedRows<ushort, CharacterLevelExperience> Keyed(IReadOnlyCollection<CharacterLevelExperience> levels) =>
        KeyedRows<ushort, CharacterLevelExperience>.Over(levels, static row => row.Level);

    public override string Describe()
        => $"{Levels.Count} levels, {ClassStats.Count} class stats, {CreateInfos.Count} create infos";
}

/// <summary>
/// Loot tables. Rolled when a creature dies, so a reload changes the next kill; drops already on
/// the ground keep what they rolled.
/// </summary>
public sealed record LootPatch(LootCatalog Catalog) : StaticDataPatch(ReloadArea.Loot)
{
    public override string Describe() => Catalog.Describe();
}

/// <summary>
/// Vendor stock (#432). Read whenever a shop lists or sells, so a reload changes the next list. On
/// its next vendor pass each town instance carries its live stock counts over by row id, and
/// every open shop hears the new list.
/// </summary>
public sealed record VendorsPatch(VendorCatalog Catalog) : StaticDataPatch(ReloadArea.Vendors)
{
    public override string Describe() => Catalog.Describe();
}

/// <summary>
/// The combat formula (#506), validated. Forward-only: the next hit resolves with the new formula, and a character's
/// derived stats change at its next stats refresh (select, gear change, level-up). Build refuses the area, naming the
/// row, so a bad row leaves the previous generation live.
/// </summary>
public sealed record CombatPatch(CombatFormula Formula) : StaticDataPatch(ReloadArea.Combat)
{
    public override string Describe() => "1 formula";

    /// <exception cref="InvalidDataException">A row is missing or out of range; the message names it.</exception>
    public static CombatPatch Build(IReadOnlyCollection<CombatFormula> formulas) =>
        new(CombatDataRules.Build(formulas));
}

/// <summary>
/// Quests (#433), validated against the creature and item templates read in the same prepare. Forward-only: a
/// character's log keeps its rows; what changes is what is offered, credited and paid from the next tick.
/// </summary>
public sealed record QuestsPatch(Quests.QuestCatalog Catalog) : StaticDataPatch(ReloadArea.Quests)
{
    public override string Describe() => Catalog.Describe();
}

/// <summary>
/// Aura templates, validated with the aura scripts loaded. Forward-only: an aura already on a unit keeps the template,
/// snapshot and stacks it was applied with; one whose template this patch drops expires on its next tick. An ability's
/// aura link is checked by the Abilities area against the auras its own prepare reads.
/// </summary>
public sealed record AurasPatch(AuraCatalog Catalog) : StaticDataPatch(ReloadArea.Auras)
{
    /// <summary>
    /// The live Resonance effects (#1180 part A2) held against these auras (<see cref="ResonanceEffectCatalog.Against" />):
    /// published with them in place of <see cref="Replaces" />, so an aura edit that breaks an effect's rules (a stun on
    /// Rimebound, a periodic heal on Guard) leaves that effect out from the next firing, and every kept effect's source is
    /// built from its aura as it now is. Null when no items generation was loaded to hold them against.
    /// </summary>
    public ResonanceEffectCatalog? Effects { get; init; }

    /// <summary>The effect catalog <see cref="Effects" /> was built from: the live one, when this patch was prepared.</summary>
    public ResonanceEffectCatalog? Replaces { get; init; }

    public override string Describe() =>
        Effects is null ? Catalog.Describe() : $"{Catalog.Describe()}, {Effects.Effects.Count} Resonance effects";
}

/// <summary>
/// Doodad templates, validated against the loot tables, auras and quests read in the same prepare and the doodad
/// scripts loaded. Forward-only: a doodad already placed keeps the template it was placed with; what changes is what
/// the next instance build places.
/// </summary>
public sealed record DoodadsPatch(DoodadCatalog Catalog) : StaticDataPatch(ReloadArea.Doodads)
{
    public override string Describe() => Catalog.Describe();
}
