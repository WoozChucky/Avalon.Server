using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.World.Filters;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Filters;

/// <summary>
/// In-map packets — MapSessionFilter is the only filter that can take them. A packet no filter accepts at arrival
/// never reaches the receive queue: it routes to Server.CallListener, which finds no handler and logs "Could not find
/// a handler for packet {PacketType}" — a silent drop plus a warning, not a wedge. (A genuine wedge needs a packet
/// accepted at arrival whose acceptance changes before the tick dispatches it — see ProcessQueueWedgeShould.)
/// Without a MapSessionFilter entry, the handler never runs.
/// </summary>
public class MapSessionFilterShould
{
    private static MapSessionFilter For(ICharacter? character)
    {
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        connection.IsGameplayAuthorized.Returns(true);
        return new MapSessionFilter(connection);
    }

    private static ICharacter CharacterOn(MapId map)
    {
        ICharacter character = Substitute.For<ICharacter>();
        character.Map.Returns(map);
        return character;
    }

    [Theory]
    [InlineData(NetworkPacketType.CMSG_INTERACT)]
    [InlineData(NetworkPacketType.CMSG_DIALOGUE_CHOOSE)]
    [InlineData(NetworkPacketType.CMSG_LOOT_PICKUP)]
    [InlineData(NetworkPacketType.CMSG_PVP_TOGGLE)]
    [InlineData(NetworkPacketType.CMSG_AURA_CANCEL)]
    [InlineData(NetworkPacketType.CMSG_ITEM_MOVE)]
    [InlineData(NetworkPacketType.CMSG_ITEM_DESTROY)]
    [InlineData(NetworkPacketType.CMSG_ITEM_USE)]
    [InlineData(NetworkPacketType.CMSG_VENDOR_BUY)]
    [InlineData(NetworkPacketType.CMSG_VENDOR_SELL)]
    [InlineData(NetworkPacketType.CMSG_VENDOR_BUYBACK)]
    [InlineData(NetworkPacketType.CMSG_PARTY_INVITE)]
    [InlineData(NetworkPacketType.CMSG_PARTY_INVITE_RESPONSE)]
    [InlineData(NetworkPacketType.CMSG_PARTY_LEAVE)]
    [InlineData(NetworkPacketType.CMSG_PARTY_KICK)]
    [InlineData(NetworkPacketType.CMSG_PARTY_PROMOTE)]
    [InlineData(NetworkPacketType.CMSG_PARTY_EXPERIENCE_MODE)]
    [InlineData(NetworkPacketType.CMSG_QUEST_ACCEPT)]
    [InlineData(NetworkPacketType.CMSG_QUEST_TURN_IN)]
    [InlineData(NetworkPacketType.CMSG_QUEST_ABANDON)]
    public void Accept_an_in_map_request_only_from_a_character_on_a_map(NetworkPacketType type)
    {
        Assert.True(For(CharacterOn(new MapId(1))).CanProcess(type));
        Assert.False(For(null).CanProcess(type));
        Assert.False(For(CharacterOn(new MapId(0))).CanProcess(type));
    }
}
