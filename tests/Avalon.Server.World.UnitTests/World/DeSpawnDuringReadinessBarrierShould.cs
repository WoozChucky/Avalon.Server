using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Characters;
using Avalon.Server.World.UnitTests.Characters;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Maps;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;
using Avalon.World.Scripts.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.World;

/// <summary>
/// The window the barrier opens. Select marks the character online in the database before the
/// client has loaded anything, so a connection that drops inside that window is the one path where
/// the row is written online and nothing has been put in an instance to write it back.
/// </summary>
public class DeSpawnDuringReadinessBarrierShould
{
    [Fact]
    public async Task Save_a_character_that_was_selected_but_never_spawned()
    {
        (Avalon.World.World world, ICharacterRepository characterRepository) = await LoadedWorldAsync();

        var row = new Character { Id = new CharacterId(7), Name = "Tester", Map = 1, Online = true };
        var entity = new CharacterEntity(NullLoggerFactory.Instance, row, new RegenConfiguration())
        {
            Data = row,
            EnteredWorld = DateTime.UtcNow
        };

        IWorldConnection connection = PendingSpawnConnection.Create(
            new PendingSpawn(entity, Substitute.For<IMapInstance>(), DateTime.UtcNow.Ticks));

        await world.DeSpawnPlayerAsync(connection);

        Assert.False(row.Online);
        await characterRepository.Received(1).UpdateAsync(row, Arg.Any<CancellationToken>());
        Assert.Null(connection.PendingSpawn);
    }

    /// <summary>
    /// The despawn runs for every disconnect, most of which never selected anything.
    /// </summary>
    [Fact]
    public async Task Save_nothing_for_a_connection_that_never_selected()
    {
        (Avalon.World.World world, ICharacterRepository characterRepository) = await LoadedWorldAsync();

        await world.DeSpawnPlayerAsync(PendingSpawnConnection.Create());

        await characterRepository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    /// <summary>
    /// The row is marked online by the spawn, not by the select: before the spawn the character is
    /// built but not in the world, and there is nothing in an instance to write the flag back.
    /// </summary>
    [Fact]
    public async Task Mark_the_row_online_when_the_character_reaches_its_instance()
    {
        (Avalon.World.World world, ICharacterRepository characterRepository) = await LoadedWorldAsync();

        var row = new Character { Id = new CharacterId(7), Name = "Tester", Map = 1, Online = false };
        var entity = new CharacterEntity(NullLoggerFactory.Instance, row, new RegenConfiguration())
        {
            Data = row,
            EnteredWorld = DateTime.UtcNow
        };
        IWorldConnection connection = PendingSpawnConnection.Create();
        connection.Character = entity;

        world.SpawnInInstance(connection, Substitute.For<IMapInstance>());

        Assert.True(row.Online);
        // The persist is fire-and-forget, but it reaches UpdateAsync before its first suspension --
        // the scope and the resolve are synchronous -- so the call is recorded by the time this runs.
        await characterRepository.Received(1).UpdateAsync(row, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// DeSpawnPlayerAsync reads the instance registry, which only exists after LoadAsync.
    /// </summary>
    private static async Task<(Avalon.World.World world, ICharacterRepository characters)> LoadedWorldAsync()
    {
        var characterRepository = Substitute.For<ICharacterRepository>();

        var scopedProvider = Substitute.For<IServiceProvider>();
        scopedProvider.GetService(typeof(ICharacterRepository)).Returns(characterRepository);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(scopedProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var worldRepository = Substitute.For<IWorldRepository>();
        worldRepository.FindByIdAsync(Arg.Any<Avalon.Domain.Auth.WorldId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new Avalon.Domain.Auth.World
            {
                Name = "test", Host = "127.0.0.1", Port = 0, MinVersion = "0.0.1", Version = "1.0.0"
            });

        var levels = Substitute.For<ICharacterLevelExperienceRepository>();
        levels.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<Avalon.Domain.World.CharacterLevelExperience>());
        var stats = Substitute.For<IClassLevelStatRepository>();
        stats.FindAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<Avalon.Domain.World.ClassLevelStat>());
        var createInfos = Substitute.For<ICharacterCreateInfoRepository>();
        createInfos.FindAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Avalon.Domain.World.CharacterCreateInfo>());
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

        var dialogue = Substitute.For<IDialogueRepository>();
        dialogue.GetAllNodesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Avalon.Domain.World.DialogueNode>>([]));
        dialogue.GetAllOptionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Avalon.Domain.World.DialogueOption>>([]));

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IChunkLayoutInstanceFactory))
            .Returns(Substitute.For<IChunkLayoutInstanceFactory>());

        var world = new Avalon.World.World(
            NullLoggerFactory.Instance,
            Options.Create(new GameConfiguration { WorldId = new Avalon.Domain.Auth.WorldId(1) }),
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
            Substitute.For<IScriptHotReloader>(),
            Substitute.For<IChunkLibrary>(),
            dialogue);

        await world.LoadAsync(CancellationToken.None);
        return (world, characterRepository);
    }
}
