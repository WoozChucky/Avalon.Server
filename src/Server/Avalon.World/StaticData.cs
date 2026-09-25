using System.Collections.Concurrent;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.World.Creatures;
using Avalon.World.Dialogue;
using Avalon.World.Localization;
using Avalon.World.Public.Dialogue;
using Avalon.World.Public.Localization;
using Avalon.World.Reload;
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
    ILoggerFactory loggerFactory)
{
    private readonly ConcurrentQueue<(StaticDataPatch Patch, TaskCompletionSource Done)> _pending = new();

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
                    texts.Count, nodes.Count, options.Count);
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

            default:
                throw new ArgumentOutOfRangeException(nameof(area), area, null);
        }
    }

    /// <summary>Assigns a prepared patch. Only called on the tick thread, or during startup load.</summary>
    public void Apply(StaticDataPatch patch)
    {
        switch (patch)
        {
            case DialoguePatch p:
                LocalizedTexts = p.Texts;
                Dialogue = p.Dialogue;
                break;
            case CreaturesPatch p:
                CreatureTemplates = p.Templates;
                CreatureBaseStats = p.BaseStats;
                CreatureRarityModifiers = p.Rarities;
                CreatureStats = p.Stats;
                break;
            case AbilitiesPatch p:
                AbilityTemplates = p.Templates;
                break;
            case ItemsPatch p:
                ItemTemplates = p.Templates;
                break;
            case ProgressionPatch p:
                CharacterLevelExperiences = p.Levels;
                ClassLevelStats = p.ClassStats;
                CharacterCreateInfos = p.CreateInfos;
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

    public IReadOnlyCollection<CharacterCreateInfo> CharacterCreateInfos { get; private set; }
    public IReadOnlyCollection<ClassLevelStat> ClassLevelStats { get; private set; }
    public IReadOnlyCollection<ItemTemplate> ItemTemplates { get; private set; }
    public IReadOnlyCollection<AbilityTemplate> AbilityTemplates { get; private set; }
    public IReadOnlyCollection<CharacterLevelExperience> CharacterLevelExperiences { get; private set; }
    public IReadOnlyCollection<CreatureBaseStat> CreatureBaseStats { get; private set; }
    public IReadOnlyCollection<CreatureRarityModifier> CreatureRarityModifiers { get; private set; }
    public IReadOnlyCollection<CreatureTemplate> CreatureTemplates { get; private set; } = [];

    /// <summary>
    /// Rebuilt whenever creature data loads. It used to be a Lazy singleton that captured the base
    /// stats once on first use and ignored every later change.
    /// </summary>
    public CreatureStatDeriver CreatureStats { get; private set; } = null!;
    public ILocalizedTextCatalog LocalizedTexts { get; private set; } = null!;
    public IDialogueCatalog Dialogue { get; private set; } = null!;
}
