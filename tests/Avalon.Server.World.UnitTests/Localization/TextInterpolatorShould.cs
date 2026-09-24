using Avalon.World.Localization;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Localization;
using Xunit;

namespace Avalon.Server.World.UnitTests.Localization;

/// <summary>
/// Named tokens and the gender-select construct. Positional {0} formatting was rejected because it
/// assumes word order is stable across languages; see the spec's "Decisions taken" section.
/// </summary>
public class TextInterpolatorShould
{
    [Fact]
    public void Substitute_The_Players_Name()
    {
        Assert.Equal(
            "Room's upstairs, Aldric.",
            TextInterpolator.Resolve("Room's upstairs, {name}.", Context(name: "Aldric")));
    }

    [Fact]
    public void Substitute_The_Level_And_The_Resolved_Class_Name()
    {
        Assert.Equal(
            "A level 7 Guerreira, then.",
            TextInterpolator.Resolve("A level {level} {class}, then.", Context(className: "Guerreira", level: 7)));
    }

    [Fact]
    public void Substitute_A_Token_Every_Time_It_Appears()
    {
        Assert.Equal(
            "Aldric? Aldric.",
            TextInterpolator.Resolve("{name}? {name}.", Context(name: "Aldric")));
    }

    [Theory]
    [InlineData(CharacterGender.Male, "Sê bem-vindo")]
    [InlineData(CharacterGender.Female, "Sê bem-vinda")]
    public void Pick_The_Branch_Matching_The_Players_Gender(CharacterGender gender, string expected)
    {
        Assert.Equal(expected,
            TextInterpolator.Resolve("Sê bem-{g:vindo|vinda}", Context(gender: gender)));
    }

    [Theory]
    [InlineData(CharacterGender.Male, "Caçador")]
    [InlineData(CharacterGender.Female, "Caçadora")]
    public void Allow_An_Empty_Branch(CharacterGender gender, string expected)
    {
        // The Portuguese masculine takes no suffix here, so the male branch is genuinely empty.
        Assert.Equal(expected, TextInterpolator.Resolve("Caçador{g:|a}", Context(gender: gender)));
    }

    [Fact]
    public void Resolve_A_Select_And_A_Token_In_One_String()
    {
        // Borin's ptPT line: the article agrees with the class name, on the same gender.
        Assert.Equal(
            "Uma Guerreira talvez se saia melhor.",
            TextInterpolator.Resolve(
                "{g:Um|Uma} {class} talvez se saia melhor.",
                Context(className: "Guerreira", gender: CharacterGender.Female)));
    }

    [Fact]
    public void Treat_A_Doubled_Brace_As_A_Literal()
    {
        Assert.Equal("{name} is a token.",
            TextInterpolator.Resolve("{{name} is a token.", Context(name: "Aldric")));
    }

    [Fact]
    public void Leave_An_Unknown_Token_Verbatim()
    {
        // Rendering {nmae} is ugly but visibly wrong and traceable. Swallowing it into a gap the
        // reader cannot diagnose is worse. The seed tests are the real defence; this is the net.
        Assert.Equal("Hello, {nmae}.", TextInterpolator.Resolve("Hello, {nmae}.", Context()));
    }

    [Fact]
    public void Leave_A_One_Sided_Select_Verbatim()
    {
        Assert.Equal("bem-{g:vindo}", TextInterpolator.Resolve("bem-{g:vindo}", Context()));
    }

    [Fact]
    public void Leave_An_Unterminated_Construct_Verbatim()
    {
        Assert.Equal("Hello, {name", TextInterpolator.Resolve("Hello, {name", Context(name: "Aldric")));
    }

    [Fact]
    public void Return_An_Empty_String_Unchanged()
    {
        Assert.Equal(string.Empty, TextInterpolator.Resolve(string.Empty, Context()));
    }

    private static TextContext Context(
        string name = "Player",
        string className = "Warrior",
        CharacterGender gender = CharacterGender.Male,
        ushort level = 1)
        => new(AccountLocale.enUS, name, className, gender, level);
}
