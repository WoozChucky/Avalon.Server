using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.World;
using Avalon.World.Handlers;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Handlers;

public class CastAbilityHandlerShould
{
    [Fact]
    public void Should_reject_cast_when_dead()
    {
        var character = Substitute.For<ICharacter>();
        character.IsDead.Returns(true);

        var connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);

        var world = Substitute.For<IWorld>();
        var handler = new CastAbilityHandler(NullLogger<CastAbilityHandler>.Instance, world);

        var packet = new CCastAbilityPacket { AbilityId = 1, TargetGuid = 2 };
        handler.Execute(connection, packet);

        connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Should_reject_cast_when_character_is_null()
    {
        var connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns((ICharacter?)null);

        var world = Substitute.For<IWorld>();
        var handler = new CastAbilityHandler(NullLogger<CastAbilityHandler>.Instance, world);

        var packet = new CCastAbilityPacket { AbilityId = 1 };
        handler.Execute(connection, packet);

        connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }
}
