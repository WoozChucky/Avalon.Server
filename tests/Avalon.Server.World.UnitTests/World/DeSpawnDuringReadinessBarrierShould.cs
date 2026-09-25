using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Characters;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Maps;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Respawn;
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
        (Avalon.World.World world, _, ICharacterSaver saver) = await LoadedWorldAsync();

        var row = new Character { Id = new CharacterId(7), Name = "Tester", Map = 1, Online = true };
        var entity = new CharacterEntity(NullLoggerFactory.Instance, row, new RegenConfiguration())
        {
            Data = row,
            EnteredWorld = DateTime.UtcNow
        };

        // The despawn save snapshots the row where it is called, so Online must already be false then.
        bool? onlineWhenSaved = null;
        saver.SaveOnDespawnAsync(entity, Arg.Any<Func<Character, CancellationToken, Task>?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            onlineWhenSaved = call.Arg<CharacterEntity>().Data!.Online;
            return Task.FromResult(true);
        });

        IWorldConnection connection = PendingSpawnConnection.Create(
            new PendingSpawn(entity, Substitute.For<IMapInstance>(), DateTime.UtcNow.Ticks));

        await world.DeSpawnPlayerAsync(connection);

        Assert.False(onlineWhenSaved);
        Assert.Null(connection.PendingSpawn);
    }

    /// <summary>
    /// Until the connection leaves the server, the tick can still run this character's queued save
    /// acknowledgements, so a despawn snapshot taken on the thread pool races them. A dead logout
    /// must therefore snapshot and join the save chain on the tick, before it goes to the database
    /// for the respawn town, and move only the snapshot's copy of the row there.
    /// </summary>
    [Fact]
    public async Task Snapshot_a_dead_logout_on_the_tick_before_the_respawn_town_is_found()
    {
        TimeSpan limit = TimeSpan.FromSeconds(5);
        var written = new TaskCompletionSource<CharacterSaveBatch>(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = Substitute.For<ICharacterSaveRepository>();
        repository.WriteAsync(Arg.Any<IReadOnlyList<CharacterSaveBatch>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                written.TrySetResult(call.Arg<IReadOnlyList<CharacterSaveBatch>>().Single());
                return Task.CompletedTask;
            });
        var saver = new CharacterSaver(repository, NullLogger<CharacterSaver>.Instance);

        var townFound = new TaskCompletionSource<MapTemplateId>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolver = Substitute.For<IRespawnTargetResolver>();
        resolver.ResolveTownAsync(new MapTemplateId(2), Arg.Any<CancellationToken>()).Returns(townFound.Task);
        var town = new MapTemplate
        {
            Id = new MapTemplateId(1), Name = "town", Description = "town", MapType = MapType.Town,
            DefaultSpawnX = 10, DefaultSpawnY = 20, DefaultSpawnZ = 30
        };

        (Avalon.World.World world, _, _) = await LoadedWorldAsync(saver, resolver, [town]);

        var row = new Character { Id = new CharacterId(7), Name = "Tester", Map = 2, Health = 100, Online = true };
        var entity = new CharacterEntity(NullLoggerFactory.Instance, row, new RegenConfiguration())
        {
            Data = row,
            EnteredWorld = DateTime.UtcNow,
        };
        entity.IsDead = true;
        Avalon.Server.World.UnitTests.Inventory.TestCharacters.InventoryFor(entity)
            .TryAdd(Avalon.Server.World.UnitTests.Inventory.TestCharacters.Potion.Id, 1);
        IWorldConnection connection = PendingSpawnConnection.Create();
        connection.Character = entity;

        Task despawn = world.DeSpawnPlayerAsync(connection);

        // The town is not known yet, and the save is already in the chain, revived on the tick.
        Assert.False(saver.WhenIdle(new CharacterId(7)).IsCompleted, "the despawn save had not joined the chain");
        Assert.False(entity.IsDead);

        // Whatever the tick does to the entity from here on is after the snapshot.
        Avalon.Server.World.UnitTests.Inventory.TestCharacters.InventoryFor(entity)
            .TryAdd(Avalon.Server.World.UnitTests.Inventory.TestCharacters.Sword.Id, 1);

        townFound.SetResult(new MapTemplateId(1));
        await despawn.WaitAsync(limit);
        CharacterSaveBatch batch = await written.Task.WaitAsync(limit);

        Assert.Equal(Avalon.Server.World.UnitTests.Inventory.TestCharacters.Potion.Id,
            Assert.Single(batch.UpsertItems).TemplateId);
        Assert.Equal((ushort)1, batch.Row.Map);
        Assert.Equal((10f, 20f, 30f), (batch.Row.X, batch.Row.Y, batch.Row.Z));
        Assert.Equal(100, batch.Row.Health);
        Assert.False(batch.Row.Online);
        Assert.Equal((ushort)2, row.Map);   // only the copy moved to the town
    }

    /// <summary>
    /// Finding the respawn town reads the World database, and the save writes the Character
    /// database. A World database failure must cost the town move, not the save: the character's
    /// items, money and offline flag still commit, at town 1's default spawn.
    /// </summary>
    [Fact]
    public async Task Still_save_a_dead_logout_when_the_respawn_town_cannot_be_found()
    {
        var town = new MapTemplate
        {
            Id = new MapTemplateId(1), Name = "town", Description = "town", MapType = MapType.Town,
            DefaultSpawnX = 10, DefaultSpawnY = 20, DefaultSpawnZ = 30
        };

        CharacterSaveBatch batch = await DeadLogoutWithBrokenTownLookupAsync([town]);

        Assert.Equal(Avalon.Server.World.UnitTests.Inventory.TestCharacters.Potion.Id,
            Assert.Single(batch.UpsertItems).TemplateId);
        Assert.Single(batch.UpsertSlots);
        Assert.Equal(500UL, batch.Row.Money);
        Assert.False(batch.Row.Online);
        Assert.Equal(100, batch.Row.Health);
        Assert.Equal((ushort)1, batch.Row.Map);
        Assert.Equal((10f, 20f, 30f), (batch.Row.X, batch.Row.Y, batch.Row.Z));
    }

    /// <summary>With no town 1 to fall back to either, the character stays where it died, and the save still commits.</summary>
    [Fact]
    public async Task Keep_the_death_position_when_neither_the_town_nor_the_fallback_is_known()
    {
        CharacterSaveBatch batch = await DeadLogoutWithBrokenTownLookupAsync([]);

        Assert.Single(batch.UpsertItems);
        Assert.Equal(500UL, batch.Row.Money);
        Assert.False(batch.Row.Online);
        Assert.Equal((ushort)2, batch.Row.Map);
        Assert.Equal((5f, 6f, 7f), (batch.Row.X, batch.Row.Y, batch.Row.Z));
    }

    private static async Task<CharacterSaveBatch> DeadLogoutWithBrokenTownLookupAsync(IReadOnlyList<MapTemplate> templates)
    {
        TimeSpan limit = TimeSpan.FromSeconds(5);
        var written = new TaskCompletionSource<CharacterSaveBatch>(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = Substitute.For<ICharacterSaveRepository>();
        repository.WriteAsync(Arg.Any<IReadOnlyList<CharacterSaveBatch>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                written.TrySetResult(call.Arg<IReadOnlyList<CharacterSaveBatch>>().Single());
                return Task.CompletedTask;
            });
        var saver = new CharacterSaver(repository, NullLogger<CharacterSaver>.Instance);

        var resolver = Substitute.For<IRespawnTargetResolver>();
        resolver.ResolveTownAsync(Arg.Any<MapTemplateId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<MapTemplateId>(new InvalidOperationException("the World database went away")));

        (Avalon.World.World world, _, _) = await LoadedWorldAsync(saver, resolver, templates);

        var row = new Character
        {
            Id = new CharacterId(7), Name = "Tester", Map = 2, Health = 100, Online = true, Money = 500,
        };
        var entity = new CharacterEntity(NullLoggerFactory.Instance, row, new RegenConfiguration())
        {
            Data = row,
            EnteredWorld = DateTime.UtcNow,
            Position = new Avalon.Common.Mathematics.Vector3(5, 6, 7),
        };
        entity.IsDead = true;
        Avalon.Server.World.UnitTests.Inventory.TestCharacters.InventoryFor(entity)
            .TryAdd(Avalon.Server.World.UnitTests.Inventory.TestCharacters.Potion.Id, 1);
        IWorldConnection connection = PendingSpawnConnection.Create();
        connection.Character = entity;

        await world.DeSpawnPlayerAsync(connection).WaitAsync(limit);

        await saver.WhenIdle(new CharacterId(7)).WaitAsync(limit);
        Assert.True(written.Task.IsCompleted, "the despawn save was dropped when the town lookup failed");
        return await written.Task.WaitAsync(limit);
    }

    /// <summary>
    /// The row is marked online by the spawn, not by the select: before the spawn the character is
    /// built but not in the world, and there is nothing in an instance to write the flag back.
    /// </summary>
    [Fact]
    public async Task Mark_the_row_online_when_the_character_reaches_its_instance()
    {
        (Avalon.World.World world, ICharacterRepository characterRepository, ICharacterSaver saver) =
            await LoadedWorldAsync();

        var row = new Character { Id = new CharacterId(7), Name = "Tester", Map = 1, Online = false };
        var entity = new CharacterEntity(NullLoggerFactory.Instance, row, new RegenConfiguration())
        {
            Data = row,
            EnteredWorld = DateTime.UtcNow
        };
        IWorldConnection connection = PendingSpawnConnection.Create();
        connection.Character = entity;

        // The save snapshots the row where it is called, so Online must already be true then.
        bool? onlineWhenSaved = null;
        saver.Save(connection, entity).Returns(call =>
        {
            onlineWhenSaved = call.Arg<CharacterEntity>().Data!.Online;
            return Task.FromResult(true);
        });

        world.SpawnInInstance(connection, Substitute.For<IMapInstance>());

        Assert.True(row.Online);
        Assert.True(onlineWhenSaved);
        // Through the character's save chain, never as a separate write that could land out of order.
        await characterRepository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    /// <summary>
    /// DeSpawnPlayerAsync reads the instance registry, which only exists after LoadAsync.
    /// </summary>
    private static async Task<(Avalon.World.World world, ICharacterRepository characters, ICharacterSaver saver)> LoadedWorldAsync(
        ICharacterSaver? realSaver = null,
        IRespawnTargetResolver? resolver = null,
        IReadOnlyList<MapTemplate>? templates = null)
    {
        var characterRepository = Substitute.For<ICharacterRepository>();
        ICharacterSaver saver = realSaver ?? Substitute.For<ICharacterSaver>();

        var scopedProvider = Substitute.For<IServiceProvider>();
        scopedProvider.GetService(typeof(ICharacterRepository)).Returns(characterRepository);
        scopedProvider.GetService(typeof(ICharacterSaver)).Returns(saver);
        scopedProvider.GetService(typeof(IRespawnTargetResolver)).Returns(resolver ?? Substitute.For<IRespawnTargetResolver>());
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

        var creatureTemplates = Substitute.For<ICreatureTemplateRepository>();
        creatureTemplates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<Avalon.Domain.World.CreatureTemplate>()));
        var baseStats = Substitute.For<ICreatureBaseStatRepository>();
        baseStats.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Avalon.Domain.World.CreatureBaseStat>>(
                [new Avalon.Domain.World.CreatureBaseStat { Level = 1, Health = 1, DamageMin = 1, DamageMax = 1, Experience = 1 }]));
        var rarities = Substitute.For<ICreatureRarityModifierRepository>();
        rarities.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Avalon.Domain.World.CreatureRarityModifier>>([]));

        var mapManager = Substitute.For<IAvalonMapManager>();
        if (templates is not null)
            mapManager.Templates.Returns(templates);

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IChunkLayoutInstanceFactory))
            .Returns(Substitute.For<IChunkLayoutInstanceFactory>());

        var world = new Avalon.World.World(
            NullLoggerFactory.Instance,
            Options.Create(new GameConfiguration { WorldId = new Avalon.Domain.Auth.WorldId(1) }),
            serviceProvider,
            worldRepository,
            mapManager,
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
            Substitute.For<IScriptHotReloader>(),
            Substitute.For<IChunkLibrary>(),
            dialogue);

        await world.LoadAsync(CancellationToken.None);
        return (world, characterRepository, saver);
    }
}
