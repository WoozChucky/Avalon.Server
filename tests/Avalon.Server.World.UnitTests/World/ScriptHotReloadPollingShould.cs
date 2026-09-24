using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character.Repositories;
using Avalon.Database.World.Repositories;
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

    [Fact]
    public async Task Take_The_Interval_From_Configuration_Rather_Than_A_Fixed_Value()
    {
        var reloader = new CountingScriptHotReloader();
        Avalon.World.World world = await BuildWorldAsync(reloader, intervalSeconds: 10);

        // Well past the 5s the interval used to be hardcoded to, and still short of the 10s asked
        // for, so a hardcoded interval polls here and a configured one does not.
        world.Update(TimeSpan.FromSeconds(6));

        Assert.Equal(0, reloader.Polls);
    }

    private static async Task<Avalon.World.World> BuildWorldAsync(
        IScriptHotReloader reloader, int intervalSeconds)
    {
        var worldRepository = Substitute.For<IWorldRepository>();
        worldRepository.FindByIdAsync(Arg.Any<Avalon.Domain.Auth.WorldId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new Avalon.Domain.Auth.World
            {
                Name = "test", Host = "127.0.0.1", Port = 0, MinVersion = "0.0.1", Version = "1.0.0"
            });

        var createInfos = Substitute.For<ICharacterCreateInfoRepository>();
        createInfos.FindAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Avalon.Domain.World.CharacterCreateInfo>());
        var stats = Substitute.For<IClassLevelStatRepository>();
        stats.FindAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Avalon.Domain.World.ClassLevelStat>());
        var levels = Substitute.For<ICharacterLevelExperienceRepository>();
        levels.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Avalon.Domain.World.CharacterLevelExperience>());
        var items = Substitute.For<IItemTemplateRepository>();
        items.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<Avalon.Domain.World.ItemTemplate>());
        var abilityTemplates = Substitute.For<IAbilityTemplateRepository>();
        abilityTemplates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<Avalon.Domain.World.AbilityTemplate>());
        var localizedText = Substitute.For<ILocalizedTextRepository>();
        localizedText.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Avalon.Domain.World.LocalizedText>>([]));
        localizedText.GetAllLocalesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Avalon.Domain.World.LocalizedTextLocale>>([]));
        localizedText.GetAllClassNamesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Avalon.Domain.World.CharacterClassName>>([]));

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IChunkLayoutInstanceFactory))
            .Returns(Substitute.For<IChunkLayoutInstanceFactory>());

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        scopeFactory.CreateScope().Returns(scope);

        var world = new Avalon.World.World(
            NullLoggerFactory.Instance,
            Options.Create(new GameConfiguration
            {
                WorldId = new Avalon.Domain.Auth.WorldId(1),
                ScriptHotReloadIntervalSeconds = intervalSeconds
            }),
            serviceProvider,
            worldRepository,
            Substitute.For<IAvalonMapManager>(),
            scopeFactory,
            createInfos,
            stats,
            items,
            abilityTemplates,
            levels,
            Substitute.For<ICreatureBaseStatRepository>(),
            Substitute.For<ICreatureRarityModifierRepository>(),
            localizedText,
            reloader,
            Substitute.For<IChunkLibrary>());

        await world.LoadAsync(CancellationToken.None);
        return world;
    }
}
