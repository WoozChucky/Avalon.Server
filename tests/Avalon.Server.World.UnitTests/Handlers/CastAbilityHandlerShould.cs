using System;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.World;
using Avalon.World.Handlers;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
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
        var handler = new CastAbilityHandler(NullLogger<CastAbilityHandler>.Instance, world, new CombatConfig());

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
        var handler = new CastAbilityHandler(NullLogger<CastAbilityHandler>.Instance, world, new CombatConfig());

        var packet = new CCastAbilityPacket { AbilityId = 1 };
        handler.Execute(connection, packet);

        connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Should_drop_cast_when_ability_not_owned()
    {
        var (handler, connection, character, _) = BuildHandler();
        // Push LastCastStartTime well outside the GCD window so the GCD check passes.
        character.LastCastStartTime.Returns(DateTime.UtcNow.AddSeconds(-10));
        character.Spells[Arg.Any<AbilityId>()].Returns((IAbility?)null);

        handler.Execute(connection, new CCastAbilityPacket { AbilityId = 99 });

        connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Should_send_SAbilityNotReadyPacket_when_on_cooldown()
    {
        var (handler, connection, character, _) = BuildHandler();
        // Push LastCastStartTime well outside the GCD window so the cooldown check is reached.
        character.LastCastStartTime.Returns(DateTime.UtcNow.AddSeconds(-10));

        var ability = Substitute.For<IAbility>();
        ability.CooldownTimer.Returns(1500);
        character.Spells[Arg.Any<AbilityId>()].Returns(ability);

        handler.Execute(connection, new CCastAbilityPacket { AbilityId = 42 });

        connection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Should_reject_cast_during_gcd_window()
    {
        var (handler, connection, character, _) = BuildHandler();
        character.LastCastStartTime.Returns(DateTime.UtcNow.AddMilliseconds(-50));

        var ability = Substitute.For<IAbility>();
        ability.CooldownTimer.Returns(0);
        character.Spells[Arg.Any<AbilityId>()].Returns(ability);

        handler.Execute(connection, new CCastAbilityPacket { AbilityId = 1 });

        connection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    private static (CastAbilityHandler handler, IWorldConnection connection, ICharacter character, IWorld world) BuildHandler()
    {
        var character = Substitute.For<ICharacter>();
        character.IsDead.Returns(false);
        var connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        // NSubstitute cannot proxy ReadOnlySpan<byte> on IAvalonCryptoSession.Encrypt — use the concrete fake.
        connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        var world = Substitute.For<IWorld>();
        var handler = new CastAbilityHandler(NullLogger<CastAbilityHandler>.Instance, world, new CombatConfig());
        return (handler, connection, character, world);
    }
}
