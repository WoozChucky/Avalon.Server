using Avalon.Domain.Characters;
using Xunit;

namespace Avalon.Shared.UnitTests.Characters;

/// <summary>
/// What a character name may be (#757): 3 to 12 ASCII letters, checked as sent. It is stored with its first letter
/// upper-case and the rest lower-case, and looked up by its upper-cased key, so names differing only in case are one.
/// </summary>
public class CharacterNameShould
{
    [Theory]
    [InlineData("Bob")]
    [InlineData("bob")]
    [InlineData("KAELA")]
    [InlineData("Abcdefghijkl")] // 12
    public void Accept_three_to_twelve_ascii_letters(string name) => Assert.True(CharacterName.IsValid(name));

    [Theory]
    [InlineData("Bo")]
    [InlineData("")]
    public void Refuse_a_name_shorter_than_three_as_too_short(string name)
    {
        Assert.False(CharacterName.IsValid(name));
        Assert.Equal(CharacterNameProblem.TooShort, CharacterName.Check(name));
    }

    [Fact]
    public void Refuse_a_name_longer_than_twelve_as_too_long()
    {
        Assert.False(CharacterName.IsValid("Abcdefghijklm"));
        Assert.Equal(CharacterNameProblem.TooLong, CharacterName.Check("Abcdefghijklm"));
    }

    [Theory]
    [InlineData("B0b")]
    [InlineData("Bob_")]
    [InlineData("Bo b")]
    [InlineData(" Bob")]
    [InlineData("Bob ")]
    [InlineData("Zoë")]
    [InlineData("Bıll")] // dotless i: upper-cases to I in .NET, so it would collide with Bill
    [InlineData("Ｂob")] // full-width B
    [InlineData("Bob-Al")]
    public void Refuse_anything_but_ascii_letters_as_invalid(string name)
    {
        Assert.False(CharacterName.IsValid(name));
        Assert.Equal(CharacterNameProblem.Invalid, CharacterName.Check(name));
    }

    [Fact]
    public void Refuse_a_missing_name_as_too_short() =>
        Assert.Equal(CharacterNameProblem.TooShort, CharacterName.Check(null));

    [Fact]
    public void Answer_none_for_a_valid_name() => Assert.Equal(CharacterNameProblem.None, CharacterName.Check("Kaela"));

    [Theory]
    [InlineData("kAELA", "Kaela")]
    [InlineData("bob", "Bob")]
    [InlineData("BOB", "Bob")]
    [InlineData("Bob", "Bob")]
    public void Store_the_first_letter_upper_case_and_the_rest_lower_case(string sent, string stored) =>
        Assert.Equal(stored, CharacterName.Display(sent));

    [Theory]
    [InlineData("Kaela", "KAELA")]
    [InlineData("kaela", "KAELA")]
    [InlineData("Bıll", "BıLL")] // only ASCII letters fold, so a non-ASCII name never reaches an ASCII one's key
    public void Key_a_name_by_its_ascii_upper_case(string name, string key) => Assert.Equal(key, CharacterName.Key(name));

    [Theory]
    [InlineData("Bob", "bob", true)]
    [InlineData("Bob", "  BOB ", true)]
    [InlineData("Bob", "Bill", false)]
    [InlineData("Bill", "Bıll", false)]
    public void Tell_whether_two_names_are_one(string a, string b, bool same) =>
        Assert.Equal(same, CharacterName.Same(a, b));
}
