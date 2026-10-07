using Avalon.Common.ValueObjects;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Social;
using Avalon.Server.World.UnitTests.Parties;
using Avalon.World;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Social;

/// <summary>
/// A deleted character drops off every ignore list (#723): the database cascades the rows, and the delete takes it off
/// every list already loaded (in the world, or selected and waiting to spawn), each of which is sent its new list.
/// </summary>
public class IgnoreDeletedCharacterShould
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Take_a_deleted_character_off_every_loaded_list()
    {
        var w = new PartyTestWorld();
        PartyClient aren = w.Online(1, "Aren");
        PartyClient tom = w.Online(3, "Tom");
        aren.Character.Ignores.Add(2, "Kaela", Now);
        aren.Character.Ignores.Add(9, "Borin", Now);

        var characters = Substitute.For<ICharacterRepository>();
        var kaela = new Character { Id = new CharacterId(2), AccountId = new AccountId(5), Name = "Kaela" };
        characters.FindByIdAndAccountAsync(Arg.Any<CharacterId>(), Arg.Any<AccountId>(), Arg.Any<CancellationToken>())
            .Returns(kaela);
        characters.DeleteForGameplayAsync(Arg.Any<Avalon.Common.GameAuth.GameplayWriteAuthority>(), Arg.Any<CharacterId>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));

        IWorldConnection deleter = Substitute.For<IWorldConnection>();
        deleter.AccountId.Returns(new AccountId(5));
        deleter.GameplayAuthority.Returns(new Avalon.Common.GameAuth.GameplayWriteAuthority(new AccountId(5), Guid.NewGuid(), 1));
        deleter.IsConnected.Returns(true);
        deleter.Character.Returns((ICharacter?)null);   // not in the world: a delete happens at character selection
        deleter.CryptoSession.Returns(new FakeAvalonCryptoSession());
        Task<bool>? deleteTask = null;
        Action<bool>? afterDelete = null;
        deleter.When(c => c.EnqueueContinuation(Arg.Any<Task<bool>>(), Arg.Any<Action<bool>>()))
            .Do(ci => { deleteTask = ci.Arg<Task<bool>>(); afterDelete = ci.Arg<Action<bool>>(); });

        // Selected and waiting on its load report: the list is loaded, the character not yet in the world.
        CharacterEntity selecting = Inventory.TestCharacters.New(4);
        selecting.Ignores.Add(2, "Kaela", Now);
        var pendingSent = new List<NetworkPacket>();
        IWorldConnection pending = Substitute.For<IWorldConnection>();
        pending.Character.Returns((ICharacter?)null);
        pending.PendingSpawn.Returns(new PendingSpawn(selecting, Substitute.For<IMapInstance>(), 0));
        pending.CryptoSession.Returns(new FakeAvalonCryptoSession());
        pending.When(c => c.Send(Arg.Any<NetworkPacket>())).Do(ci => pendingSent.Add(ci.Arg<NetworkPacket>()));

        IWorldServer server = Substitute.For<IWorldServer>();
        server.Connections.Returns([aren.Connection, tom.Connection, pending, deleter]);

        new CharacterDeletetHandler(NullLogger<CharacterDeletetHandler>.Instance, characters, server)
            .Execute(deleter, new CCharacterDeletePacket { CharacterId = 2 });

        Assert.NotNull(deleteTask);
        afterDelete!(await deleteTask!);

        Assert.Empty(selecting.Ignores.Entries);
        Assert.Contains(pendingSent, p => p.Header.Type == NetworkPacketType.SMSG_IGNORE_LIST);

        Assert.Equal([9u], aren.Character.Ignores.Entries.Select(e => e.Id));
        SIgnoreListPacket list = Assert.Single(aren.Read<SIgnoreListPacket>(NetworkPacketType.SMSG_IGNORE_LIST));
        Assert.Equal([9u], list.Characters.Select(c => c.CharacterId));
        Assert.Empty(tom.Sent);
    }
}
