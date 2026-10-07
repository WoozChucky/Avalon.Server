using Avalon.Combat;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character.Repositories;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Loot;
using Avalon.Network.Packets.Party;
using Avalon.Network.Packets.Quest;
using Avalon.Network.Packets.Social;
using Avalon.Network.Packets.Vendor;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Characters;
using Avalon.World;
using Avalon.World.Chat;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Handlers;
using Avalon.World.Inventory;
using Avalon.World.Loot;
using Avalon.World.Parties;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Instances;
using Avalon.World.Quests;
using Avalon.World.Respawn;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Handlers;

/// <summary>
/// The guards each request handler implements for itself, one row per handler: what it does for a connection
/// that holds no character, and for one whose character is built and waiting on the readiness barrier.
/// </summary>
public class HandlerGuardsShould
{
    /// <summary>
    /// A connection that holds no character (one back at character selection, say) is answered nothing. Without
    /// the guard each handler would throw, or answer an error, on the missing character.
    /// </summary>
    [Theory]
    [InlineData(NetworkPacketType.CMSG_CAST_ABILITY)]
    [InlineData(NetworkPacketType.CMSG_CHAT_MESSAGE)]
    [InlineData(NetworkPacketType.CMSG_DIALOGUE_CHOOSE)]
    [InlineData(NetworkPacketType.CMSG_INTERACT)]
    [InlineData(NetworkPacketType.CMSG_LOOT_PICKUP)]
    [InlineData(NetworkPacketType.CMSG_PARTY_LEAVE)]
    [InlineData(NetworkPacketType.CMSG_QUEST_ACCEPT)]
    [InlineData(NetworkPacketType.CMSG_VENDOR_BUY)]
    [InlineData(NetworkPacketType.CMSG_VENDOR_SELL)]
    [InlineData(NetworkPacketType.CMSG_VENDOR_BUYBACK)]
    public void Answer_nothing_to_a_connection_with_no_character(NetworkPacketType request)
    {
        IWorld world = Substitute.For<IWorld>();
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns((ICharacter?)null);
        connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        ICharacterEconomy economy = Substitute.For<ICharacterEconomy>();

        switch (request)
        {
            case NetworkPacketType.CMSG_CAST_ABILITY:
                new CastAbilityHandler(NullLogger<CastAbilityHandler>.Instance, world, new CombatConfig(), TimeProvider.System)
                    .Execute(connection, new CCastAbilityPacket { AbilityId = 1 });
                break;
            case NetworkPacketType.CMSG_CHAT_MESSAGE:
                new ChatMessageHandler(world, Substitute.For<ICommandDispatcher>(),
                        new ChatRateLimiter(Options.Create(new GameConfiguration()), TimeProvider.System))
                    .Execute(connection, new CChatMessagePacket { Message = "Hello", DateTime = DateTime.UtcNow });
                break;
            case NetworkPacketType.CMSG_DIALOGUE_CHOOSE:
                new DialogueChooseHandler(NullLogger<DialogueChooseHandler>.Instance, world)
                    .Execute(connection, new CDialogueChoosePacket { TargetGuid = 7, NodeId = 1, OptionId = 1 });
                break;
            case NetworkPacketType.CMSG_INTERACT:
                new InteractHandler(NullLogger<InteractHandler>.Instance, world)
                    .Execute(connection, new CInteractPacket { TargetGuid = 7 });
                break;
            case NetworkPacketType.CMSG_LOOT_PICKUP:
                new LootPickupHandler(NullLogger<LootPickupHandler>.Instance, world, economy, TimeProvider.System)
                    .Execute(connection, new CLootPickupPacket { LootGuid = 1 });
                break;
            case NetworkPacketType.CMSG_PARTY_LEAVE:
                new PartyLeaveHandler(
                        new PartyService(Options.Create(new GameConfiguration()), TimeProvider.System,
                            NullLogger<PartyService>.Instance),
                        NullLogger<PartyLeaveHandler>.Instance)
                    .Execute(connection, new CPartyLeavePacket());
                break;
            case NetworkPacketType.CMSG_QUEST_ACCEPT:
                new QuestAcceptHandler(
                        new QuestService(world, Substitute.For<IServiceProvider>(), economy, Substitute.For<ILootRandom>(),
                            TimeProvider.System, NullLogger<QuestService>.Instance),
                        NullLogger<QuestAcceptHandler>.Instance)
                    .Execute(connection, new CQuestAcceptPacket { QuestId = 1, NpcGuid = 7 });
                break;
            case NetworkPacketType.CMSG_VENDOR_BUY:
                new VendorBuyHandler(NullLogger<VendorBuyHandler>.Instance, world, economy, NoQuestProgress.Instance,
                        TimeProvider.System)
                    .Execute(connection, new CVendorBuyPacket { RequestId = 1, Sequence = 1 });
                break;
            case NetworkPacketType.CMSG_VENDOR_SELL:
                new VendorSellHandler(NullLogger<VendorSellHandler>.Instance, world, economy)
                    .Execute(connection, new CVendorSellPacket { RequestId = 1, BagSlot = 0 });
                break;
            case NetworkPacketType.CMSG_VENDOR_BUYBACK:
                new VendorBuybackHandler(NullLogger<VendorBuybackHandler>.Instance, world, economy)
                    .Execute(connection, new CVendorBuybackPacket { RequestId = 1, Index = 0 });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request), request, null);
        }

        connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    /// <summary>
    /// A character waiting on the readiness barrier has selected: it is built, and its row is already marked
    /// online. Before the barrier that state lasted a few database round trips; it now lasts as long as a client
    /// takes to load a map. So the list, create, delete and select handlers each close such a connection and read
    /// nothing. A delete would delete the character being spawned; a second select would overwrite the pending
    /// spawn and leave that row online with nothing holding the entity that would have cleared it.
    /// </summary>
    [Theory]
    [InlineData(NetworkPacketType.CMSG_CHARACTER_LIST)]
    [InlineData(NetworkPacketType.CMSG_CHARACTER_CREATE)]
    [InlineData(NetworkPacketType.CMSG_CHARACTER_DELETE)]
    [InlineData(NetworkPacketType.CMSG_CHARACTER_SELECTED)]
    public void Close_a_connection_whose_character_is_waiting_to_spawn(NetworkPacketType request)
    {
        IWorldConnection connection = PendingSpawnConnection.Create(
            new PendingSpawn(PendingSpawnConnection.Character(), Substitute.For<IMapInstance>(), DateTime.UtcNow.Ticks));
        ICharacterRepository characters = Substitute.For<ICharacterRepository>();
        IWorld world = Substitute.For<IWorld>();

        switch (request)
        {
            case NetworkPacketType.CMSG_CHARACTER_LIST:
                new CharacterListHandler(NullLogger<CharacterListHandler>.Instance, characters, world)
                    .Execute(connection, new CCharacterListPacket());
                break;
            case NetworkPacketType.CMSG_CHARACTER_CREATE:
                new CharacterCreateHandler(NullLogger<CharacterCreateHandler>.Instance, characters,
                        Substitute.For<IItemIdAllocator>(), world)
                    .Execute(connection, new CCharacterCreatePacket());
                break;
            case NetworkPacketType.CMSG_CHARACTER_DELETE:
                new CharacterDeletetHandler(NullLogger<CharacterDeletetHandler>.Instance, characters)
                    .Execute(connection, new CCharacterDeletePacket());
                break;
            case NetworkPacketType.CMSG_CHARACTER_SELECTED:
                new CharacterSelectHandler(NullLogger<CharacterSelectHandler>.Instance, NullLoggerFactory.Instance,
                        characters, Substitute.For<ICharacterInventoryRepository>(),
                        Substitute.For<IItemInstanceRepository>(), Substitute.For<ICharacterAbilityRepository>(),
                        Substitute.For<IChunkLibrary>(), world, Substitute.For<IRespawnTargetResolver>(),
                        Options.Create(new RegenConfiguration()), Substitute.For<IAccountRepository>(),
                        Substitute.For<ICharacterSaver>(), Substitute.For<IWorldServer>())
                    .Execute(connection, new CCharacterSelectedPacket());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request), request, null);
        }

        connection.Received().Close(Arg.Any<bool>());
        Assert.Empty(characters.ReceivedCalls());
        connection.DidNotReceiveWithAnyArgs().SetPendingSpawn(default!, default!, default);
    }
}
