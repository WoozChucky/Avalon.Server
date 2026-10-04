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
        connection.IsGameplayAuthorized.Returns(true);
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
    public void Accept_Pvp_Toggle_For_A_Character_On_A_Map() =>
        Assert.True(For(CharacterOnMap()).CanProcess(NetworkPacketType.CMSG_PVP_TOGGLE));

    [Fact]
    public void Reject_Pvp_Toggle_Without_A_Character() =>
        Assert.False(For(null).CanProcess(NetworkPacketType.CMSG_PVP_TOGGLE));

    [Fact]
    public void Accept_an_aura_cancel_for_a_character_on_a_map_and_none_without_one()
    {
        Assert.True(For(CharacterOnMap()).CanProcess(NetworkPacketType.CMSG_AURA_CANCEL));
        Assert.False(For(null).CanProcess(NetworkPacketType.CMSG_AURA_CANCEL));
        Assert.False(For(CharacterOffMap()).CanProcess(NetworkPacketType.CMSG_AURA_CANCEL));
        Assert.True(MapSessionFilter.IsMapPacket(NetworkPacketType.CMSG_AURA_CANCEL));
    }

    [Fact]
    public void Reject_Loot_Pickup_Without_A_Character()
    {
        Assert.False(For(null).CanProcess(NetworkPacketType.CMSG_LOOT_PICKUP));
    }

    [Fact]
    public void Accept_Item_Move_For_A_Character_On_A_Map()
    {
        // Without this entry the handler never runs: the request is dropped with a warning.
        Assert.True(For(CharacterOnMap()).CanProcess(NetworkPacketType.CMSG_ITEM_MOVE));
    }

    [Fact]
    public void Accept_Item_Destroy_For_A_Character_On_A_Map()
    {
        Assert.True(For(CharacterOnMap()).CanProcess(NetworkPacketType.CMSG_ITEM_DESTROY));
    }

    [Fact]
    public void Reject_Item_Requests_Without_A_Character()
    {
        Assert.False(For(null).CanProcess(NetworkPacketType.CMSG_ITEM_MOVE));
        Assert.False(For(null).CanProcess(NetworkPacketType.CMSG_ITEM_DESTROY));
    }

    [Fact]
    public void Reject_Item_Requests_Off_Map()
    {
        Assert.False(For(CharacterOffMap()).CanProcess(NetworkPacketType.CMSG_ITEM_MOVE));
        Assert.False(For(CharacterOffMap()).CanProcess(NetworkPacketType.CMSG_ITEM_DESTROY));
    }

    [Fact]
    public void Accept_an_item_use_for_a_character_on_a_map_and_none_without_one()
    {
        Assert.True(For(CharacterOnMap()).CanProcess(NetworkPacketType.CMSG_ITEM_USE));
        Assert.False(For(null).CanProcess(NetworkPacketType.CMSG_ITEM_USE));
        Assert.False(For(CharacterOffMap()).CanProcess(NetworkPacketType.CMSG_ITEM_USE));
    }

    [Fact]
    public void Accept_Vendor_Requests_For_A_Character_On_A_Map()
    {
        MapSessionFilter filter = For(CharacterOnMap());

        Assert.True(filter.CanProcess(NetworkPacketType.CMSG_VENDOR_BUY));
        Assert.True(filter.CanProcess(NetworkPacketType.CMSG_VENDOR_SELL));
        Assert.True(filter.CanProcess(NetworkPacketType.CMSG_VENDOR_BUYBACK));
    }

    [Fact]
    public void Reject_Vendor_Requests_Without_A_Character()
    {
        Assert.False(For(null).CanProcess(NetworkPacketType.CMSG_VENDOR_BUY));
        Assert.False(For(null).CanProcess(NetworkPacketType.CMSG_VENDOR_SELL));
        Assert.False(For(null).CanProcess(NetworkPacketType.CMSG_VENDOR_BUYBACK));
    }

    [Fact]
    public void Reject_Vendor_Requests_Off_Map()
    {
        Assert.False(For(CharacterOffMap()).CanProcess(NetworkPacketType.CMSG_VENDOR_BUY));
        Assert.False(For(CharacterOffMap()).CanProcess(NetworkPacketType.CMSG_VENDOR_SELL));
        Assert.False(For(CharacterOffMap()).CanProcess(NetworkPacketType.CMSG_VENDOR_BUYBACK));
    }

    [Theory]
    [InlineData(NetworkPacketType.CMSG_PARTY_INVITE)]
    [InlineData(NetworkPacketType.CMSG_PARTY_INVITE_RESPONSE)]
    [InlineData(NetworkPacketType.CMSG_PARTY_LEAVE)]
    [InlineData(NetworkPacketType.CMSG_PARTY_KICK)]
    [InlineData(NetworkPacketType.CMSG_PARTY_PROMOTE)]
    [InlineData(NetworkPacketType.CMSG_PARTY_EXPERIENCE_MODE)]
    public void Accept_every_party_request_for_a_character_on_a_map_and_none_without_one(NetworkPacketType type)
    {
        Assert.True(For(CharacterOnMap()).CanProcess(type));
        Assert.False(For(null).CanProcess(type));
    }

    [Theory]
    [InlineData(NetworkPacketType.CMSG_QUEST_ACCEPT)]
    [InlineData(NetworkPacketType.CMSG_QUEST_TURN_IN)]
    [InlineData(NetworkPacketType.CMSG_QUEST_ABANDON)]
    public void Accept_every_quest_request_for_a_character_on_a_map_and_none_without_one(NetworkPacketType type)
    {
        Assert.True(For(CharacterOnMap()).CanProcess(type));
        Assert.False(For(null).CanProcess(type));
    }
}
