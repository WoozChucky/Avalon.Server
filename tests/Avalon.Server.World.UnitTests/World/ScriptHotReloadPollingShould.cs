using Avalon.Database.Auth.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Maps;
using Avalon.World.Scripts.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.World;

/// <summary>
/// The world tick polls the hot reloader on an interval rather than every tick. These hold it to
/// the interval configuration asks for: polling every tick would ask the filesystem 60 times a
/// second, and never polling would mean a saved script is never picked up.
/// </summary>
public class ScriptHotReloadPollingShould
{
    /// <summary>
    /// Counts polls. Hand-rolled rather than substituted because the method has an <c>out</c>
    /// parameter, which NSubstitute cannot express in a Received() assertion — the same reason
    /// FakeAvalonCryptoSession exists.
    /// </summary>
    private sealed class CountingScriptHotReloader : IScriptHotReloader
    {
        public int Polls { get; private set; }

        public void Update(out List<Type> scriptTypes)
        {
            Polls++;
            scriptTypes = [];
        }

        // The world tick drives reloads by polling Update; these are the watcher's own lifecycle and
        // are not exercised here.
        public event ScriptsHotReloadedEventHandler? ScriptsHotReloaded;

        public void Start() => ScriptsHotReloaded?.Invoke([]);

        public void Stop() { }
    }

    [Fact]
    public async Task Poll_The_Reloader_Once_The_Configured_Interval_Has_Elapsed()
    {
        var reloader = new CountingScriptHotReloader();
        Avalon.World.World world = await BuildWorldAsync(reloader, intervalSeconds: 1);

        world.Update(TimeSpan.FromMilliseconds(600));
        Assert.Equal(0, reloader.Polls);

        world.Update(TimeSpan.FromMilliseconds(600));
        Assert.Equal(1, reloader.Polls);
    }

    /// <summary>
    /// A loaded world polling <paramref name="reloader" />. Its instances come from
    /// <paramref name="chunkLayoutFactory" /> for the templates <paramref name="mapManager" /> lists
    /// (none when omitted), and scripts a reload builds get a null logger factory. The world logs to
    /// <paramref name="loggerFactory" /> and holds <paramref name="parties" /> when given.
    /// </summary>
    internal static async Task<Avalon.World.World> BuildWorldAsync(
        IScriptHotReloader reloader, int intervalSeconds,
        IAvalonMapManager? mapManager = null, IChunkLayoutInstanceFactory? chunkLayoutFactory = null,
        Avalon.World.Parties.PartyService? parties = null, Microsoft.Extensions.Logging.ILoggerFactory? loggerFactory = null,
        Avalon.World.Threading.TickThreadGuard? tickThread = null)
    {
        IWorldRepository worldRepository = Substitute.For<IWorldRepository>();
        worldRepository.FindByIdAsync(Arg.Any<Avalon.Domain.Auth.WorldId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new Avalon.Domain.Auth.World
            {
                Name = "test",
                Host = "127.0.0.1",
                Port = 0,
                MinVersion = "0.0.1",
                Version = "1.0.0"
            });

        ICharacterCreateInfoRepository createInfos = Substitute.For<ICharacterCreateInfoRepository>();
        createInfos.FindAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Avalon.Domain.World.CharacterCreateInfo>());
        IClassLevelStatRepository stats = Substitute.For<IClassLevelStatRepository>();
        stats.FindAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Avalon.Domain.World.ClassLevelStat>());
        ICharacterLevelExperienceRepository levels = Substitute.For<ICharacterLevelExperienceRepository>();
        levels.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Avalon.Domain.World.CharacterLevelExperience>());
        IItemTemplateRepository items = Substitute.For<IItemTemplateRepository>();
        items.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<Avalon.Domain.World.ItemTemplate>());
        IAbilityTemplateRepository abilityTemplates = Substitute.For<IAbilityTemplateRepository>();
        abilityTemplates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<Avalon.Domain.World.AbilityTemplate>());
        ILocalizedTextRepository localizedText = Substitute.For<ILocalizedTextRepository>();
        localizedText.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Avalon.Domain.World.LocalizedText>>([]));
        localizedText.GetAllLocalesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Avalon.Domain.World.LocalizedTextLocale>>([]));
        localizedText.GetAllClassNamesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Avalon.Domain.World.CharacterClassName>>([]));

        IDialogueRepository dialogue = Substitute.For<IDialogueRepository>();
        dialogue.GetAllNodesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Avalon.Domain.World.DialogueNode>>([]));
        dialogue.GetAllOptionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Avalon.Domain.World.DialogueOption>>([]));

        ICreatureTemplateRepository creatureTemplates = Substitute.For<ICreatureTemplateRepository>();
        creatureTemplates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<Avalon.Domain.World.CreatureTemplate>()));
        ICreatureBaseStatRepository baseStats = Substitute.For<ICreatureBaseStatRepository>();
        baseStats.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Avalon.Domain.World.CreatureBaseStat>>(
                [new Avalon.Domain.World.CreatureBaseStat { Level = 1, Health = 1, DamageMin = 1, DamageMax = 1, Experience = 1 }]));
        ICreatureRarityModifierRepository rarities = Substitute.For<ICreatureRarityModifierRepository>();
        rarities.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Avalon.Domain.World.CreatureRarityModifier>>([]));

        IServiceProvider serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IChunkLayoutInstanceFactory))
            .Returns(chunkLayoutFactory ?? Substitute.For<IChunkLayoutInstanceFactory>());
        serviceProvider.GetService(typeof(Microsoft.Extensions.Logging.ILoggerFactory))
            .Returns(NullLoggerFactory.Instance);

        IServiceScopeFactory scopeFactory = Substitute.For<IServiceScopeFactory>();
        IServiceScope scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        scopeFactory.CreateScope().Returns(scope);

        var world = new Avalon.World.World(
            loggerFactory ?? NullLoggerFactory.Instance,
            Options.Create(new GameConfiguration
            {
                WorldId = new Avalon.Domain.Auth.WorldId(1),
                ScriptHotReloadIntervalSeconds = intervalSeconds
            }),
            serviceProvider,
            worldRepository,
            mapManager ?? Substitute.For<IAvalonMapManager>(),
            scopeFactory,
            createInfos,
            stats,
            items,
            abilityTemplates,
            levels,
            creatureTemplates,
            baseStats,
            rarities,
            localizedText,
            reloader,
            Substitute.For<IChunkLibrary>(),
            dialogue, LootRepositories.Empty(), Avalon.Server.World.UnitTests.Chat.ChatLimits.Off(), parties: parties, tickThread: tickThread);

        await world.LoadAsync(CancellationToken.None);
        return world;
    }
}
