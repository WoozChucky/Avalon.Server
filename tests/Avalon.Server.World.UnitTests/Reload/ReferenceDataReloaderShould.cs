using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World;
using Avalon.World.Reload;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Reload;

/// <summary>
/// The reloader orchestrates PrepareAsync/ApplyOnNextTickAsync over one or more areas and reports
/// what happened. Areas are independent: one area failing must not stop the others, and a failed
/// area must leave its data exactly as it was. Built over a real StaticData (as in
/// StaticDataReloadShould), with IWorld substituted only to hand back that StaticData.
/// </summary>
public class ReferenceDataReloaderShould
{
    private sealed class Repos
    {
        public List<CreatureTemplate> Templates = [];
        public List<CreatureBaseStat> BaseStats =
            [new() { Level = 1, Health = 40, DamageMin = 3, DamageMax = 5, Experience = 15 }];
        public bool FailBaseStats;

        /// <summary>
        /// Makes the base-stat read throw OperationCanceledException instead of the ordinary
        /// InvalidOperationException FailBaseStats throws — stands in for a caller's own token
        /// already being cancelled by the time PrepareAsync observes it.
        /// </summary>
        public bool CancelBaseStats;

        public List<ItemTemplate> Items = [];

        /// <summary>When set, an items read takes its snapshot, signals ItemsReadStarted and waits for the gate.</summary>
        public Task? ItemsGate;
        public TaskCompletionSource ItemsReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<AbilityTemplate> Abilities = [];
        public List<LocalizedText> Texts = [];
        public List<CharacterLevelExperience> Levels = [];
        public List<ClassLevelStat> ClassStats = [];
        public List<CharacterCreateInfo> CreateInfos = [];
    }

    private static CreatureTemplate Template(ulong id) => new()
    {
        Id = new CreatureTemplateId(id),
        Name = $"creature-{id}",
        MinLevel = 1,
        MaxLevel = 1,
        Rarity = Avalon.World.Public.Enums.CreatureRarity.Normal,
        HealthModifier = 1f,
        DamageModifier = 1f,
        ExperienceModifier = 1f
    };

    [Fact]
    public async Task Reports_A_Success_With_The_Patchs_Summary()
    {
        (StaticData data, _) = await LoadedData(creatureCount: 1);
        StaticDataPatch expected = await data.PrepareAsync(ReloadArea.Dialogue);
        IReferenceDataReloader reloader = Reloader(data);

        Task<ReloadReport> reload = reloader.ReloadAsync([ReloadArea.Dialogue]);
        ReloadReport report = await RunToCompletion(data, reload);

        ReloadOutcome outcome = Assert.Single(report.Outcomes);
        Assert.Equal(ReloadArea.Dialogue, outcome.Area);
        Assert.True(outcome.Succeeded);
        Assert.Equal(expected.Describe(), outcome.Summary);
        Assert.Null(outcome.Error);
    }

    [Fact]
    public async Task Keep_Going_After_A_Failing_Area()
    {
        (StaticData data, Repos repos) = await LoadedData(creatureCount: 1);
        repos.FailBaseStats = true;
        repos.Items = [new ItemTemplate { Id = new ItemTemplateId(1), Name = "item-1" }];
        IReferenceDataReloader reloader = Reloader(data);

        Task<ReloadReport> reload = reloader.ReloadAsync([ReloadArea.Creatures, ReloadArea.Items]);
        ReloadReport report = await RunToCompletion(data, reload);

        Assert.Equal(2, report.Outcomes.Count);

        ReloadOutcome creatures = report.Outcomes[0];
        Assert.Equal(ReloadArea.Creatures, creatures.Area);
        Assert.False(creatures.Succeeded);
        Assert.NotNull(creatures.Error);

        ReloadOutcome items = report.Outcomes[1];
        Assert.Equal(ReloadArea.Items, items.Area);
        Assert.True(items.Succeeded);
        Assert.Single(data.ItemTemplates);
    }

    [Fact]
    public async Task Change_Nothing_For_A_Failing_Area()
    {
        (StaticData data, Repos repos) = await LoadedData(creatureCount: 1);
        IReadOnlyCollection<CreatureTemplate> before = data.CreatureTemplates;
        repos.FailBaseStats = true;
        IReferenceDataReloader reloader = Reloader(data);

        Task<ReloadReport> reload = reloader.ReloadAsync([ReloadArea.Creatures]);
        await RunToCompletion(data, reload);

        Assert.Same(before, data.CreatureTemplates);
    }

    [Fact]
    public async Task Rethrow_The_Callers_Own_Cancellation_Instead_Of_Recording_It_As_A_Failure()
    {
        // A cancelled ct that reaches PrepareAsync surfaces as an OperationCanceledException from
        // the repository read. That must unwind ReloadAsync as a cancellation, not get swallowed
        // into a "failed" outcome — see the precedent in PresenceSnapshotService.
        (StaticData data, Repos repos) = await LoadedData(creatureCount: 1);
        repos.CancelBaseStats = true;
        IReferenceDataReloader reloader = Reloader(data);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reloader.ReloadAsync([ReloadArea.Creatures], cts.Token).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Apply_The_Later_Reloads_Read_Last_When_Two_Reloads_Of_One_Area_Overlap()
    {
        (StaticData data, Repos repos) = await LoadedData(creatureCount: 1);
        IReferenceDataReloader reloader = Reloader(data);
        repos.Items = [new ItemTemplate { Id = new ItemTemplateId(1), Name = "old" }];
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        repos.ItemsGate = gate.Task;

        // The first reload reads "old" and stalls in the database read.
        Task<ReloadReport> first = reloader.ReloadAsync([ReloadArea.Items]);
        await repos.ItemsReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // The row is saved again and the second reload reads it, without a stall.
        repos.ItemsGate = null;
        repos.Items = [new ItemTemplate { Id = new ItemTemplateId(1), Name = "new" }];
        Task<ReloadReport> second = reloader.ReloadAsync([ReloadArea.Items]);

        // Ticks run while the first read is still stalled, then the stalled read finishes.
        for (int tick = 0; tick < 10; tick++)
        {
            data.ApplyPending();
            await Task.Delay(10);
        }

        gate.SetResult();
        for (int tick = 0; tick < 100 && !(first.IsCompleted && second.IsCompleted); tick++)
        {
            data.ApplyPending();
            await Task.Delay(10);
        }

        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("new", data.ItemTemplates.Single().Name);
    }

    private static IReferenceDataReloader Reloader(StaticData data)
    {
        var world = Substitute.For<IWorld>();
        world.Data.Returns(data);
        return new ReferenceDataReloader(world, NullLoggerFactory.Instance.CreateLogger<ReferenceDataReloader>());
    }

    private static async Task<ReloadReport> RunToCompletion(StaticData data, Task<ReloadReport> reload)
    {
        // Areas apply one per tick in sequence, so keep ticking until the reload finishes.
        for (int tick = 0; tick < 50 && !reload.IsCompleted; tick++)
        {
            data.ApplyPending();
            await Task.Delay(10);
        }

        return await reload.WaitAsync(TimeSpan.FromSeconds(5));
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
            .Returns(_ => repos.CancelBaseStats
                ? Task.FromException<IReadOnlyCollection<CreatureBaseStat>>(new OperationCanceledException())
                : repos.FailBaseStats
                    ? Task.FromException<IReadOnlyCollection<CreatureBaseStat>>(new InvalidOperationException("db down"))
                    : Task.FromResult<IReadOnlyCollection<CreatureBaseStat>>(repos.BaseStats.ToList()));

        var rarities = Substitute.For<ICreatureRarityModifierRepository>();
        rarities.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CreatureRarityModifier>>(
            [
                new CreatureRarityModifier
                {
                    Rarity = Avalon.World.Public.Enums.CreatureRarity.Normal,
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
            .Returns(async _ =>
            {
                List<ItemTemplate> snapshot = repos.Items.ToList();
                if (repos.ItemsGate is { } gate)
                {
                    repos.ItemsReadStarted.TrySetResult();
                    await gate;
                }

                return snapshot;
            });

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
            localizedText, dialogue, LootRepositories.Empty(), NullLoggerFactory.Instance);

        await data.LoadAsync();
        return (data, repos);
    }
}
