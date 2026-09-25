using System.Net;
using System.Net.Sockets;
using Avalon.Common;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Characters;
using Avalon.Hosting.Networking;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Maps;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Scripts.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Characters;

/// <summary>
/// The per-connection state NPC dialogue needs: the row's persisted Gender (previously unreachable
/// from the world runtime), a connection's Locale (defaults to enUS so dialogue renders before the
/// account read lands or if it fails), and CurrentDialogue (cleared on despawn so a stale
/// (npc, node) pair cannot outlive the connection that opened it).
/// </summary>
public class CharacterLocaleAndGenderShould
{
    [Theory]
    [InlineData(CharacterGender.Male)]
    [InlineData(CharacterGender.Female)]
    public void Expose_The_Rows_Gender_On_The_Entity(CharacterGender gender)
    {
        // Gender is persisted but was previously unreachable from the world runtime, so the
        // gender-select construct had nothing to resolve against.
        // Id must be set: CharacterEntity's constructor builds Guid from it via an implicit
        // CharacterId -> uint conversion, which throws NullReferenceException against a null Id.
        var row = new Character { Id = 1u, Name = "Aldric", Gender = gender, Level = 1 };

        var entity = new CharacterEntity(NullLoggerFactory.Instance, row, new RegenConfiguration())
        {
            Data = row
        };

        Assert.Equal(gender, entity.Gender);
    }

    [Fact]
    public void Default_A_Connections_Locale_To_enUS()
    {
        // So dialogue renders in English even before the account read lands, or if it fails.
        // Driven against the real connection, not a substitute: a substituted interface property
        // just echoes back whatever NSubstitute's auto-property returns for an unset value, which
        // would not exercise the production `= AccountLocale.enUS` initializer at all.
        var server = Substitute.For<IWorldServer, IServerBase>();
        ((IServerBase)server).SendBufferCapacity.Returns(256);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var clientSide = new TcpClient();
        clientSide.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint!).Port);
        TcpClient serverSide = listener.AcceptTcpClient();
        listener.Stop();

        var connection = new Avalon.World.WorldConnection(
            server, clientSide, NullLoggerFactory.Instance, Substitute.For<IPacketReader>());
        try
        {
            Assert.Equal(AccountLocale.enUS, connection.Locale);
        }
        finally
        {
            connection.Close();
            connection.Dispose();
            serverSide.Dispose();
        }
    }

    /// <summary>
    /// A stale (npc, node) pair surviving a disconnect would let a reconnecting player resume a
    /// conversation with an NPC that may no longer be in their (new) instance. CurrentTargetGuid is
    /// never cleared anywhere in the codebase -- it is only set (TargetUnitHandler) and read
    /// (ThreatBroadcastService) -- so there is no existing reset hook to piggyback on. Instead this
    /// pins CurrentDialogue's own clear, in World.DeSpawnPlayerAsync, the single hook that covers
    /// every disconnect path (logout, alt-F4, TCP timeout).
    /// </summary>
    [Fact]
    public async Task Clear_An_Open_Conversation_When_The_Player_Is_Despawned()
    {
        (Avalon.World.World world, _) = await LoadedWorldAsync();

        var row = new Character { Id = new CharacterId(7), Name = "Tester", Map = 1, Online = true };
        var entity = new CharacterEntity(NullLoggerFactory.Instance, row, new RegenConfiguration())
        {
            Data = row,
            EnteredWorld = DateTime.UtcNow
        };

        IWorldConnection connection = PendingSpawnConnection.Create(
            new PendingSpawn(entity, Substitute.For<IMapInstance>(), DateTime.UtcNow.Ticks));
        connection.CurrentDialogue = (new ObjectGuid(ObjectType.Creature, 42), new DialogueNodeId(1));

        await world.DeSpawnPlayerAsync(connection);

        Assert.Null(connection.CurrentDialogue);
    }

    /// <summary>
    /// DeSpawnPlayerAsync reads the instance registry, which only exists after LoadAsync. Mirrors
    /// DeSpawnDuringReadinessBarrierShould's fixture -- duplicated rather than shared, matching this
    /// codebase's existing convention of per-file fixture helpers (e.g. EmptyStaticData(Async) in
    /// CharacterSelectHandlerShould / CharacterSelectChainShould).
    /// </summary>
    private static async Task<(Avalon.World.World world, ICharacterRepository characters)> LoadedWorldAsync()
    {
        var characterRepository = Substitute.For<ICharacterRepository>();

        var scopedProvider = Substitute.For<IServiceProvider>();
        scopedProvider.GetService(typeof(ICharacterRepository)).Returns(characterRepository);
        scopedProvider.GetService(typeof(Avalon.World.Persistence.ICharacterSaver)).Returns(Substitute.For<Avalon.World.Persistence.ICharacterSaver>());
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
            creatureTemplates,
            baseStats,
            rarities,
            localizedText,
            Substitute.For<IScriptHotReloader>(),
            Substitute.For<IChunkLibrary>(),
            dialogue);

        await world.LoadAsync(CancellationToken.None);
        return (world, characterRepository);
    }
}
