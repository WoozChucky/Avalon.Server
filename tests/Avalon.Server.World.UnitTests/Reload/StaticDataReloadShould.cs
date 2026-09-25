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
/// The guarantee the whole design rests on: nothing a packet reads may change except at the very
/// top of World.Update, because packets are processed on that same thread. Prepare (database reads
/// and catalog construction) runs on the thread pool and touches nothing live; apply (property
/// assignment) runs only from ApplyPending, called at the top of World.Update.
/// </summary>
public class StaticDataReloadShould
{
    private sealed class Repos
    {
        public List<CreatureTemplate> Templates = [];
        public List<CreatureBaseStat> BaseStats =
            [new() { Level = 1, Health = 40, DamageMin = 3, DamageMax = 5, Experience = 15 }];
        public bool FailBaseStats;
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
        // The guarantee the whole design rests on: nothing a packet reads changes until the top
        // of the next tick, because packets are processed on that same thread.
        (StaticData data, Repos repos) = await LoadedData(creatureCount: 1);
        repos.Templates = [Template(1), Template(2)];

        StaticDataPatch patch = await data.PrepareAsync(ReloadArea.Creatures);
        Task applied = data.ApplyOnNextTickAsync(patch);

        Assert.Single(data.CreatureTemplates);
        Assert.False(applied.IsCompleted);

        data.ApplyPending();
        await applied;

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

        await Assert.ThrowsAsync<NotSupportedException>(() => bad);
        await good;
        Assert.Equal(2, data.CreatureTemplates.Count);
    }

    private sealed record UnknownPatch() : StaticDataPatch(ReloadArea.Items)
    {
        public override string Describe() => "unknown";
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
            .Returns(Task.FromResult<IReadOnlyCollection<CharacterCreateInfo>>([]));

        var classLevelStats = Substitute.For<IClassLevelStatRepository>();
        classLevelStats.FindAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<ClassLevelStat>>([]));

        var itemTemplates = Substitute.For<IItemTemplateRepository>();
        itemTemplates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<ItemTemplate>()));

        var abilityTemplates = Substitute.For<IAbilityTemplateRepository>();
        abilityTemplates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<AbilityTemplate>()));

        var characterLevelExperiences = Substitute.For<ICharacterLevelExperienceRepository>();
        characterLevelExperiences.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CharacterLevelExperience>>([]));

        var localizedText = Substitute.For<ILocalizedTextRepository>();
        localizedText.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LocalizedText>>([]));
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
