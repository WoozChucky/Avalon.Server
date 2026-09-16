using Avalon.Network.Packets.Abstractions;
using Avalon.World.Filters;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Filters;

/// <summary>
/// ProcessQueue peeks: a packet no filter will take is not dropped, it stays at the head of the
/// queue and stops everything behind it. That makes which filter covers CMSG_CHARACTER_LOADED a
/// question about whether a connection keeps working, not only about whether it gets dispatched.
/// </summary>
public class WorldSessionFilterShould
{
    private static WorldSessionFilter For(ICharacter? character)
    {
        var connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        return new WorldSessionFilter(connection);
    }

    [Fact]
    public void Accept_the_load_report_while_the_character_is_still_pending()
    {
        Assert.True(For(null).CanProcess(NetworkPacketType.CMSG_CHARACTER_LOADED));
    }

    /// <summary>
    /// The barrier can expire between the report being queued and the pass that dispatches it. The
    /// map filter does not list the report, so a session filter that turned it down once the
    /// character existed would leave it stuck at the head of the queue forever.
    /// </summary>
    [Fact]
    public void Accept_the_load_report_after_the_character_has_spawned()
    {
        Assert.True(For(Substitute.For<ICharacter>()).CanProcess(NetworkPacketType.CMSG_CHARACTER_LOADED));
    }

    [Fact]
    public void Still_turn_down_in_map_packets_before_a_character_exists()
    {
        Assert.False(For(null).CanProcess(NetworkPacketType.CMSG_PLAYER_INPUT));
    }
}
