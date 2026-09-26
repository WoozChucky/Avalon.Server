using System.Collections.Concurrent;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.World.Creatures;
using Avalon.World.Dialogue;
using Avalon.World.Localization;
using Avalon.World.Loot;
using Avalon.World.Public.Dialogue;
using Avalon.World.Public.Localization;
using Avalon.World.Reload;
using Avalon.World.Vendors;
using Microsoft.Extensions.Logging;

namespace Avalon.World;

public class StaticData(
    ICharacterCreateInfoRepository characterCreateInfoRepository,
    IClassLevelStatRepository classLevelStatRepository,
    IItemTemplateRepository itemTemplateRepository,
    IAbilityTemplateRepository abilityTemplateRepository,
    ICharacterLevelExperienceRepository characterLevelExperienceRepository,
    ICreatureTemplateRepository creatureTemplateRepository,
    ICreatureBaseStatRepository creatureBaseStatRepository,
    ICreatureRarityModifierRepository creatureRarityModifierRepository,
    ILocalizedTextRepository localizedTextRepository,
    IDialogueRepository dialogueRepository,
    ILootTableRepository lootTableRepository,
    ILoggerFactory loggerFactory,
    IVendorStockRepository? vendorStockRepository = null)
{
    private readonly ConcurrentQueue<(StaticDataPatch Patch, TaskCompletionSource Done)> _pending = new();

    // One volatile reference per area. Apply replaces exactly one of these per area, never a member
    // of one — so a reader on any thread that takes the field once sees one whole generation, never
    // a template from a new patch paired with a deriver from an old one. `volatile` is what makes a
    // write on the tick thread visible, fully published, to a read on a thread-pool thread; without
    // it a reader could observe a torn or stale reference.
    //
    // Nothing is assigned here at construction, deliberately: before LoadAsync runs there is no data
    // and nothing reads it, so there is no "empty default patch" to invent.
    private volatile DialoguePatch? _dialogue;
    private volatile CreaturesPatch? _creatures;
    private volatile AbilitiesPatch? _abilities;
    private volatile ItemsPatch? _items;
    private volatile ProgressionPatch? _progression;
    private volatile LootPatch? _loot;
    private volatile VendorsPatch? _vendors;

    /// <summary>
    /// Reads the database and builds a whole patch for one area. Runs on the thread pool and
    /// touches nothing live; a failure throws and leaves the current data exactly as it was.
    /// </summary>
    public async Task<StaticDataPatch> PrepareAsync(ReloadArea area, CancellationToken ct = default)
    {
        switch (area)
        {
            case ReloadArea.Dialogue:
            {
                var texts = await localizedTextRepository.GetAllAsync(ct);
                var locales = await localizedTextRepository.GetAllLocalesAsync(ct);
                var classNames = await localizedTextRepository.GetAllClassNamesAsync(ct);
                var nodes = await dialogueRepository.GetAllNodesAsync(ct);
                var options = await dialogueRepository.GetAllOptionsAsync(ct);

                return new DialoguePatch(
                    new LocalizedTextCatalog(texts, locales, classNames, loggerFactory),
                    new DialogueCatalog(nodes, options, loggerFactory),
                    texts.Count, nodes.Count, options.Count,
                    new DialogueActions(nodes, options));
            }

            case ReloadArea.Creatures:
            {
                IReadOnlyCollection<CreatureTemplate> templates =
                    (await creatureTemplateRepository.FindAllAsync(false, ct)).AsReadOnly();
                var baseStats = await creatureBaseStatRepository.GetAllAsync(ct);
                var rarities = await creatureRarityModifierRepository.GetAllAsync(ct);

                // Built from the collections just read, never from the ones currently applied —
                // capturing the applied ones is exactly the trap this work removes.
                return new CreaturesPatch(templates, baseStats, rarities,
                    new CreatureStatDeriver(baseStats, rarities, loggerFactory));
            }

            case ReloadArea.Abilities:
                return new AbilitiesPatch((await abilityTemplateRepository.FindAllAsync(false, ct)).AsReadOnly());

            case ReloadArea.Items:
                return new ItemsPatch((await itemTemplateRepository.FindAllAsync(false, ct)).AsReadOnly());

            case ReloadArea.Progression:
                return new ProgressionPatch(
                    await characterLevelExperienceRepository.GetAllAsync(ct),
                    await classLevelStatRepository.FindAllAsync(ct),
                    await characterCreateInfoRepository.FindAllAsync(ct));

            case ReloadArea.Loot:
                return new LootPatch(new LootCatalog(await lootTableRepository.GetAllAsync(ct), loggerFactory));

            case ReloadArea.Vendors:
            {
                // Validated against the item templates read here, never the ones applied, so the
                // rows and the items they name are one generation. No repository (tests that build
                // StaticData without one) is an empty catalog.
                IReadOnlyCollection<VendorStock> rows = vendorStockRepository is null
                    ? Array.Empty<VendorStock>()
                    : await vendorStockRepository.GetAllAsync(ct);
                IReadOnlyCollection<ItemTemplate> items = (await itemTemplateRepository.FindAllAsync(false, ct)).AsReadOnly();
                return new VendorsPatch(new VendorCatalog(rows, items, loggerFactory));
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(area), area, null);
        }
    }

    /// <summary>
    /// Assigns a prepared patch. Only called on the tick thread, or during startup load. Each area
    /// replaces exactly one volatile reference — never assigns a member of one — so the area is
    /// published as a single whole generation.
    /// </summary>
    public void Apply(StaticDataPatch patch)
    {
        switch (patch)
        {
            case DialoguePatch p:
                _dialogue = p;
                break;
            case CreaturesPatch p:
                _creatures = p;
                break;
            case AbilitiesPatch p:
                _abilities = p;
                break;
            case ItemsPatch p:
                _items = p;
                break;
            case ProgressionPatch p:
                _progression = p;
                break;
            case LootPatch p:
                _loot = p;
                break;
            case VendorsPatch p:
                _vendors = p;
                break;
            default:
                throw new NotSupportedException($"No apply for {patch.GetType().Name}");
        }
    }

    /// <summary>
    /// Queues a patch for the top of the next world tick and completes once it is live. Continuations
    /// run asynchronously, so completing this on the tick thread never runs a caller's code there.
    /// </summary>
    public Task ApplyOnNextTickAsync(StaticDataPatch patch)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending.Enqueue((patch, done));
        return done.Task;
    }

    /// <summary>
    /// Applies every queued patch. Called at the top of World.Update, on the tick thread, before any
    /// instance ticks. One failing patch faults its own task and does not stop the rest.
    /// </summary>
    public void ApplyPending()
    {
        while (_pending.TryDequeue(out var item))
        {
            try
            {
                Apply(item.Patch);
                item.Done.TrySetResult();
            }
            catch (Exception ex)
            {
                item.Done.TrySetException(ex);
            }
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        // Startup: there is no tick yet, so applying directly is correct.
        foreach (ReloadArea area in Enum.GetValues<ReloadArea>())
        {
            Apply(await PrepareAsync(area, cancellationToken));
        }
    }

    // Read-through properties. Each reads its area's volatile field, so an existing caller sees
    // exactly the same shape as before — but a caller that needs more than one member of the same
    // area together (Creatures below) must not chain two of these, since a reload could land between
    // them and pair a new template with an old deriver, or vice versa.
    public IReadOnlyCollection<CharacterCreateInfo> CharacterCreateInfos => _progression!.CreateInfos;
    public IReadOnlyCollection<ClassLevelStat> ClassLevelStats => _progression!.ClassStats;
    public IReadOnlyCollection<ItemTemplate> ItemTemplates => _items!.Templates;
    public IReadOnlyCollection<AbilityTemplate> AbilityTemplates => _abilities!.Templates;
    public IReadOnlyCollection<CharacterLevelExperience> CharacterLevelExperiences => _progression!.Levels;
    public IReadOnlyCollection<CreatureBaseStat> CreatureBaseStats => _creatures!.BaseStats;
    public IReadOnlyCollection<CreatureRarityModifier> CreatureRarityModifiers => _creatures!.Rarities;
    public IReadOnlyCollection<CreatureTemplate> CreatureTemplates => _creatures!.Templates;

    /// <summary>
    /// Rebuilt whenever creature data loads. It used to be a Lazy singleton that captured the base
    /// stats once on first use and ignored every later change.
    /// </summary>
    public CreatureStatDeriver CreatureStats => _creatures!.Stats;

    public ILocalizedTextCatalog LocalizedTexts => _dialogue!.Texts;
    public IDialogueCatalog Dialogue => _dialogue!.Dialogue;

    /// <summary>
    /// What dialogue options do (spec #463). The same generation as <see cref="Dialogue" />: both
    /// are read on the tick, where no reload can land between two reads.
    /// </summary>
    public DialogueActions DialogueActions => _dialogue!.Actions;

    /// <summary>
    /// Read on the tick when a creature dies. One reference, so a kill sees one whole generation of
    /// tables even if a reload is queued.
    /// </summary>
    public LootCatalog Loot => _loot!.Catalog;

    /// <summary>
    /// Vendor stock (#432). Read on the tick, when a shop lists, sells, or a town instance runs its
    /// vendor pass; one reference, so one generation.
    /// </summary>
    public VendorCatalog Vendors => _vendors!.Catalog;

    /// <summary>
    /// Snapshot accessor for a reader that needs more than one member of the creatures area
    /// together — chiefly <c>CreatureSpawner.Spawn</c>, which runs off the tick thread (instance
    /// construction awaits before it spawns). A caller must read this once into a local and use
    /// <c>Templates</c>/<c>Stats</c> from that local: two separate reads of <see cref="CreatureTemplates"/>
    /// and <see cref="CreatureStats"/> could each observe a different generation if a reload lands
    /// in between, pairing a new template with an old deriver or the reverse.
    /// </summary>
    public CreaturesPatch Creatures => _creatures!;
}
