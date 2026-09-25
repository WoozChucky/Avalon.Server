using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.World;
using Avalon.World.Public.Enums;
using Avalon.World.Reload;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Reload;

/// <summary>
/// The guarantee the whole design rests on: nothing a map-pass packet reads may change except at
/// the very top of World.Update, because map-pass packets (movement, attack, chat) are processed
/// on that same thread, inside the instance loop that runs after ApplyPending. (Session-pass
/// packets — character create/select, CMSG_PONG — run earlier, in WorldServer.Update, before
/// World.Update is even called, so for them the same guarantee holds a tick later — it is not true
/// that no packet at all is processed before ApplyPending, only that none can see a torn area.)
/// Prepare (database reads and catalog construction) runs on the thread pool and touches nothing
/// live or shared; apply swaps one volatile reference per area and runs only from ApplyPending,
/// called at the top of World.Update. A reader off the tick thread — creature spawning during
/// instance construction, which awaits before it spawns — gets the same one-generation guarantee
/// from the area's single published reference (see StaticData.Creatures), not from tick ordering.
/// </summary>
public class StaticDataReloadShould
{
    private sealed class Repos
    {
        public List<CreatureTemplate> Templates = [];
        public List<CreatureBaseStat> BaseStats =
            [new() { Level = 1, Health = 40, DamageMin = 3, DamageMax = 5, Experience = 15 }];
        public bool FailBaseStats;

        public List<ItemTemplate> Items = [];
        public List<AbilityTemplate> Abilities = [];
        public List<LocalizedText> Texts = [];
        public List<CharacterLevelExperience> Levels = [];
        public List<ClassLevelStat> ClassStats = [];
        public List<CharacterCreateInfo> CreateInfos = [];
    }

    private static CreatureTemplate Template(ulong id, float healthModifier = 1f) => new()
    {
        Id = new CreatureTemplateId(id),
        Name = $"creature-{id}",
        MinLevel = 1,
        MaxLevel = 1,
        Rarity = CreatureRarity.Normal,
        HealthModifier = healthModifier,
        DamageModifier = 1f,
        ExperienceModifier = 1f
    };

    [Fact]
    public async Task Keep_Serving_The_Old_Data_Until_The_Tick_Applies_It()
    {
        // The guarantee the whole design rests on: nothing a map-pass packet reads changes until
        // the top of the next tick, because map-pass packets are processed on that same thread,
        // after ApplyPending runs (see the class doc comment for the session-pass caveat).
        (StaticData data, Repos repos) = await LoadedData(creatureCount: 1);
        repos.Templates = [Template(1), Template(2)];

        StaticDataPatch patch = await data.PrepareAsync(ReloadArea.Creatures);
        Task applied = data.ApplyOnNextTickAsync(patch);

        Assert.Single(data.CreatureTemplates);
        Assert.False(applied.IsCompleted);

        data.ApplyPending();
        await applied.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, data.CreatureTemplates.Count);
    }

    [Fact]
    public async Task Apply_Every_Reload_Queued_Before_The_Same_Tick()
    {
        // Two game masters at once. A single slot like _pendingHotReload would drop one.
        (StaticData data, Repos repos) = await LoadedData(creatureCount: 1);

        Task first = data.ApplyOnNextTickAsync(await data.PrepareAsync(ReloadArea.Creatures));
        Task second = data.ApplyOnNextTickAsync(await data.PrepareAsync(ReloadArea.Items));

        data.ApplyPending();

        // Bounded, so a dropped reload fails as a clear timeout rather than hanging the run.
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        await second.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Change_Nothing_When_Prepare_Fails()
    {
        (StaticData data, Repos repos) = await LoadedData(creatureCount: 1);
        IReadOnlyCollection<CreatureTemplate> before = data.CreatureTemplates;
        repos.FailBaseStats = true;

        await Assert.ThrowsAnyAsync<Exception>(() => data.PrepareAsync(ReloadArea.Creatures));

        Assert.Same(before, data.CreatureTemplates);
    }

    [Fact]
    public async Task Survive_An_Apply_That_Throws_And_Still_Apply_The_Next()
    {
        // A throw escaping ApplyPending would escape World.Update and stop the world. It must
        // fault only its own reload.
        (StaticData data, Repos repos) = await LoadedData(creatureCount: 1);
        repos.Templates = [Template(1), Template(2)];

        Task bad = data.ApplyOnNextTickAsync(new UnknownPatch());
        Task good = data.ApplyOnNextTickAsync(await data.PrepareAsync(ReloadArea.Creatures));

        data.ApplyPending();

        // Bounded, so a dropped or never-applied reload fails as a clear timeout rather than
        // hanging the run — the same reasoning as Apply_Every_Reload_Queued_Before_The_Same_Tick.
        await Assert.ThrowsAsync<NotSupportedException>(() => bad.WaitAsync(TimeSpan.FromSeconds(5)));
        await good.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, data.CreatureTemplates.Count);
    }

    private sealed record UnknownPatch() : StaticDataPatch(ReloadArea.Items)
    {
        public override string Describe() => "unknown";
    }

    // One test per area: preparing and applying changed data must be visible through that area's
    // public properties. Each area now replaces exactly one volatile reference (see StaticData.cs),
    // so these also stand in for the per-area "keeps the stale reference forever" trap — a mutation
    // such as "_progression ??= p;" would leave every property below reporting the original,
    // pre-reload (empty) data instead of what was just prepared.

    [Fact]
    public async Task Make_Reloaded_Dialogue_Visible_Through_Its_Properties()
    {
        (StaticData data, _) = await LoadedData(creatureCount: 1);
        var textsBefore = data.LocalizedTexts;
        var dialogueBefore = data.Dialogue;

        data.Apply(await data.PrepareAsync(ReloadArea.Dialogue));

        // PrepareAsync always builds fresh catalog instances, so a correct apply produces new
        // references even with unchanged underlying rows — a stale ("??=") apply would not.
        Assert.NotSame(textsBefore, data.LocalizedTexts);
        Assert.NotSame(dialogueBefore, data.Dialogue);
    }

    [Fact]
    public async Task Make_Reloaded_Creatures_Visible_Through_Its_Properties()
    {
        (StaticData data, Repos repos) = await LoadedData(creatureCount: 1);
        repos.Templates = [Template(1), Template(2)];

        data.Apply(await data.PrepareAsync(ReloadArea.Creatures));

        Assert.Equal(2, data.CreatureTemplates.Count);
    }

    [Fact]
    public async Task Make_Reloaded_Abilities_Visible_Through_Its_Properties()
    {
        (StaticData data, Repos repos) = await LoadedData(creatureCount: 1);
        Assert.Empty(data.AbilityTemplates);
        repos.Abilities = [new AbilityTemplate { Id = new AbilityId(1), Name = "ability-1", SpellScript = "" }];

        data.Apply(await data.PrepareAsync(ReloadArea.Abilities));

        Assert.Single(data.AbilityTemplates);
    }

    [Fact]
    public async Task Make_Reloaded_Items_Visible_Through_Its_Properties()
    {
        (StaticData data, Repos repos) = await LoadedData(creatureCount: 1);
        Assert.Empty(data.ItemTemplates);
        repos.Items = [new ItemTemplate { Id = new ItemTemplateId(1), Name = "item-1" }];

        data.Apply(await data.PrepareAsync(ReloadArea.Items));

        Assert.Single(data.ItemTemplates);
    }

    [Fact]
    public async Task Make_Reloaded_Progression_Visible_Through_Its_Properties()
    {
        (StaticData data, Repos repos) = await LoadedData(creatureCount: 1);
        Assert.Empty(data.CharacterLevelExperiences);
        Assert.Empty(data.ClassLevelStats);
        Assert.Empty(data.CharacterCreateInfos);

        repos.Levels = [new CharacterLevelExperience { Level = 2, Experience = 500 }];
        repos.ClassStats = [new ClassLevelStat { Class = CharacterClass.Warrior, Level = 1, BaseHp = 10 }];
        repos.CreateInfos = [new CharacterCreateInfo { Class = CharacterClass.Warrior, Map = 1 }];

        data.Apply(await data.PrepareAsync(ReloadArea.Progression));

        // Asserting all three together is what would have caught U4/U5 (dropping ClassLevelStats or
        // CreateInfos from the progression area) under the old per-member Apply. Under the new
        // single-reference-per-area Apply there is no longer a line to drop independently — the
        // whole ProgressionPatch is swapped as one — so the mutation this now stands in for is a
        // stale whole-area apply ("_progression ??= p;"), which would leave all three still empty.
        Assert.Single(data.CharacterLevelExperiences);
        Assert.Single(data.ClassLevelStats);
        Assert.Single(data.CharacterCreateInfos);
    }

    private static async Task<(StaticData Data, Repos Repos)> LoadedData(int creatureCount)
    {
        var repos = new Repos
        {
            Templates = Enumerable.Range(1, creatureCount).Select(i => Template((ulong)i)).ToList()
        };

        var templates = Substitute.For<ICreatureTemplateRepository>();
        templates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(repos.Templates.ToList()));

        var baseStats = Substitute.For<ICreatureBaseStatRepository>();
        baseStats.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(_ => repos.FailBaseStats
                ? Task.FromException<IReadOnlyCollection<CreatureBaseStat>>(new InvalidOperationException("db down"))
                : Task.FromResult<IReadOnlyCollection<CreatureBaseStat>>(repos.BaseStats.ToList()));

        var rarities = Substitute.For<ICreatureRarityModifierRepository>();
        rarities.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CreatureRarityModifier>>(
            [
                new CreatureRarityModifier
                {
                    Rarity = CreatureRarity.Normal,
                    HealthMultiplier = 1f, DamageMultiplier = 1f, ExperienceMultiplier = 1f
                }
            ]));

        var createInfos = Substitute.For<ICharacterCreateInfoRepository>();
        createInfos.FindAllAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyCollection<CharacterCreateInfo>>(repos.CreateInfos.ToList()));

        var classLevelStats = Substitute.For<IClassLevelStatRepository>();
        classLevelStats.FindAllAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyCollection<ClassLevelStat>>(repos.ClassStats.ToList()));

        var itemTemplates = Substitute.For<IItemTemplateRepository>();
        itemTemplates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(repos.Items.ToList()));

        var abilityTemplates = Substitute.For<IAbilityTemplateRepository>();
        abilityTemplates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(repos.Abilities.ToList()));

        var characterLevelExperiences = Substitute.For<ICharacterLevelExperienceRepository>();
        characterLevelExperiences.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyCollection<CharacterLevelExperience>>(repos.Levels.ToList()));

        var localizedText = Substitute.For<ILocalizedTextRepository>();
        localizedText.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyCollection<LocalizedText>>(repos.Texts.ToList()));
        localizedText.GetAllLocalesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LocalizedTextLocale>>([]));
        localizedText.GetAllClassNamesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CharacterClassName>>([]));

        var dialogue = Substitute.For<IDialogueRepository>();
        dialogue.GetAllNodesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<DialogueNode>>([]));
        dialogue.GetAllOptionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<DialogueOption>>([]));

        StaticData data = new(createInfos, classLevelStats, itemTemplates, abilityTemplates,
            characterLevelExperiences, templates, baseStats, rarities,
            localizedText, dialogue, NullLoggerFactory.Instance);

        await data.LoadAsync();
        return (data, repos);
    }
}
