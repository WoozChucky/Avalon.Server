using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.World.Filters;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Filters;

/// <summary>
/// ProcessQueue peeks: a packet neither filter will take is not dropped, it stays at the head of the
/// queue and stops everything behind it. CMSG_INTERACT and CMSG_DIALOGUE_CHOOSE are in-map packets —
/// MapSessionFilter is the only filter that can take them, and until it does, an interact silently
/// wedges the sending connection rather than merely failing to reach InteractHandler.
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
}
