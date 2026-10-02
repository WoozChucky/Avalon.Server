using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Public;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Characters;

/// <summary>The one online lookup (#717), shared by the party invite and the whisper.</summary>
public class OnlineCharactersShould
{
    private readonly OnlineCharacters _online = new();

    private static (IWorldConnection Connection, CharacterEntity Character) Connected(uint id, string name)
    {
        CharacterEntity character = TestCharacters.New(id);
        character.Name = name;
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        return (connection, character);
    }

    [Fact]
    public void Find_a_character_by_name_ignoring_case_and_surrounding_spaces()
    {
        (IWorldConnection connection, _) = Connected(7, "Kaela");
        _online.Add(connection);

        Assert.Same(connection, _online.ByName("  kAELA "));
        Assert.Same(connection, _online.ById(7));
        Assert.True(_online.IsOnline(7));
    }

    /// <summary>
    /// Names fold only ASCII letters, as the stored key does (#757): a dotless i upper-cases to I in .NET, and would
    /// otherwise find "Bill" from a name the database would never match.
    /// </summary>
    [Fact]
    public void Find_no_character_by_a_name_that_matches_only_outside_ascii()
    {
        (IWorldConnection connection, _) = Connected(7, "Bill");
        _online.Add(connection);

        Assert.Null(_online.ByName("Bıll"));
        Assert.Same(connection, _online.ByName("bILL"));
    }

    [Fact]
    public void Forget_a_character_that_goes_offline()
    {
        (IWorldConnection connection, CharacterEntity character) = Connected(7, "Kaela");
        _online.Add(connection);

        Assert.True(_online.Remove(connection, character));

        Assert.Null(_online.ByName("Kaela"));
        Assert.False(_online.IsOnline(7));
    }

    [Fact]
    public void Ignore_a_stale_connection_going_offline()
    {
        (IWorldConnection old, CharacterEntity character) = Connected(7, "Kaela");
        _online.Add(old);
        IWorldConnection current = Substitute.For<IWorldConnection>();
        current.Character.Returns(character);
        _online.Add(current);

        Assert.False(_online.Remove(old, character));

        Assert.Same(current, _online.ByName("Kaela"));
    }
}
