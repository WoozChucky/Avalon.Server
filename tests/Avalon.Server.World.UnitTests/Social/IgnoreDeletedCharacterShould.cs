using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Social;
using Avalon.Server.World.UnitTests.Parties;
using Avalon.World.Handlers;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Social;

/// <summary>
/// A deleted character drops off every ignore list (#723): the database cascades the rows, and the delete takes it off
/// the lists of the characters online now, each of which is sent its new list.
/// </summary>
public class IgnoreDeletedCharacterShould
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Take_a_deleted_character_off_the_lists_of_everyone_online()
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
        characters.DeleteAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        IWorldConnection deleter = Substitute.For<IWorldConnection>();
        deleter.AccountId.Returns(new AccountId(5));
        deleter.Character.Returns((ICharacter?)null);   // not in the world: a delete happens at character selection
        deleter.CryptoSession.Returns(new FakeAvalonCryptoSession());
        deleter.When(c => c.EnqueueContinuation(Arg.Any<Task<Character?>>(), Arg.Any<Action<Character?>>()))
            .Do(ci => ci.Arg<Action<Character?>>()(ci.Arg<Task<Character?>>().Result));
        deleter.When(c => c.EnqueueContinuation(Arg.Any<Task>(), Arg.Any<Action>()))
            .Do(ci => ci.Arg<Action>()());

        new CharacterDeletetHandler(NullLogger<CharacterDeletetHandler>.Instance, characters, w.Parties.Online)
            .Execute(deleter, new CCharacterDeletePacket { CharacterId = 2 });

        Assert.Equal([9u], aren.Character.Ignores.Entries.Select(e => e.Id));
        SIgnoreListPacket list = Assert.Single(aren.Read<SIgnoreListPacket>(NetworkPacketType.SMSG_IGNORE_LIST));
        Assert.Equal([9u], list.Characters.Select(c => c.CharacterId));
        Assert.Empty(tom.Sent);
    }
}
