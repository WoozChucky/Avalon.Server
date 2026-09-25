using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.World.Filters;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Filters;

/// <summary>
/// CMSG_INTERACT and CMSG_DIALOGUE_CHOOSE are in-map packets — MapSessionFilter is the only filter
/// that can take them. A packet no filter accepts at arrival never reaches the receive queue: it
/// routes to Server.CallListener, which finds no handler and logs "Could not find a handler for
/// packet {PacketType}" — a silent drop plus a warning, not a wedge. (A genuine wedge needs a packet
/// accepted at arrival whose acceptance changes before the tick dispatches it — see
/// ProcessQueueWedgeShould.) Without a MapSessionFilter entry, the handler still never runs, which is
/// the bug this branch had to fix.
/// </summary>
public class MapSessionFilterShould
{
    private static MapSessionFilter For(ICharacter? character)
    {
        var connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        return new MapSessionFilter(connection);
    }

    private static ICharacter CharacterOnMap()
    {
        var character = Substitute.For<ICharacter>();
        character.Map.Returns(new MapId(1));
        return character;
    }

    private static ICharacter CharacterOffMap()
    {
        var character = Substitute.For<ICharacter>();
        character.Map.Returns(new MapId(0));
        return character;
    }

    [Fact]
    public void Accept_Interact_For_A_Character_On_A_Map()
    {
        Assert.True(For(CharacterOnMap()).CanProcess(NetworkPacketType.CMSG_INTERACT));
    }

    [Fact]
    public void Accept_Dialogue_Choose_For_A_Character_On_A_Map()
    {
        Assert.True(For(CharacterOnMap()).CanProcess(NetworkPacketType.CMSG_DIALOGUE_CHOOSE));
    }

    [Fact]
    public void Reject_Interact_Without_A_Character()
    {
        Assert.False(For(null).CanProcess(NetworkPacketType.CMSG_INTERACT));
    }

    [Fact]
    public void Reject_Dialogue_Choose_Without_A_Character()
    {
        Assert.False(For(null).CanProcess(NetworkPacketType.CMSG_DIALOGUE_CHOOSE));
    }

    [Fact]
    public void Reject_Interact_Off_Map()
    {
        Assert.False(For(CharacterOffMap()).CanProcess(NetworkPacketType.CMSG_INTERACT));
    }

    [Fact]
    public void Reject_Dialogue_Choose_Off_Map()
    {
        Assert.False(For(CharacterOffMap()).CanProcess(NetworkPacketType.CMSG_DIALOGUE_CHOOSE));
    }

    [Fact]
    public void Accept_Loot_Pickup_For_A_Character_On_A_Map()
    {
        // Without this entry the handler never runs: the request is dropped with a warning.
        Assert.True(For(CharacterOnMap()).CanProcess(NetworkPacketType.CMSG_LOOT_PICKUP));
    }

    [Fact]
    public void Reject_Loot_Pickup_Without_A_Character()
    {
        Assert.False(For(null).CanProcess(NetworkPacketType.CMSG_LOOT_PICKUP));
    }
}
