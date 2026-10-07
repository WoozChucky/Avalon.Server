using Avalon.Common.Accounts;
using Avalon.World.Localization;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Localization;

namespace Avalon.Server.World.UnitTests.Localization;

/// <summary>
/// Named tokens and the gender-select construct. Positional {0} formatting was rejected because it
/// assumes word order is stable across languages; see the spec's "Decisions taken" section.
/// </summary>
public class TextInterpolatorShould
{
    [Theory]
    [InlineData("A level {level} {class}, then.", CharacterGender.Male, "A level 7 Guerreira, then.")]
    [InlineData("{name}? {name}.", CharacterGender.Male, "Aldric? Aldric.")]   // every time it appears
    [InlineData("Sê bem-{g:vindo|vinda}", CharacterGender.Male, "Sê bem-vindo")]
    [InlineData("Sê bem-{g:vindo|vinda}", CharacterGender.Female, "Sê bem-vinda")]
    // The Portuguese masculine takes no suffix here, so the male branch is genuinely empty.
    [InlineData("Caçador{g:|a}", CharacterGender.Male, "Caçador")]
    [InlineData("Caçador{g:|a}", CharacterGender.Female, "Caçadora")]
    // Borin's ptPT line: the article agrees with the class name, on the same gender.
    [InlineData("{g:Um|Uma} {class} talvez se saia melhor.", CharacterGender.Female,
        "Uma Guerreira talvez se saia melhor.")]
    [InlineData("{{name} is a token.", CharacterGender.Male, "{name} is a token.")]   // a doubled brace is a literal
    public void Substitute_Tokens_And_Pick_The_Branch_Matching_The_Players_Gender(
        string text, CharacterGender gender, string expected)
    {
        Assert.Equal(expected, TextInterpolator.Resolve(text, Context(gender)));
    }

    /// <summary>
    /// Rendering {nmae} is ugly but visibly wrong and traceable. Swallowing it into a gap the reader cannot
    /// diagnose is worse. The seed tests are the real defence; this is the net.
    /// </summary>
    [Theory]
    [InlineData("Hello, {nmae}.")]   // an unknown token
    [InlineData("bem-{g:vindo}")]    // a one-sided select
    [InlineData("Hello, {name")]     // an unterminated construct
    [InlineData("")]
    public void Leave_What_It_Cannot_Resolve_Verbatim(string text)
    {
        Assert.Equal(text, TextInterpolator.Resolve(text, Context(CharacterGender.Male)));
    }

    private static TextContext Context(CharacterGender gender)
        => new(AccountLocale.enUS, "Aldric", "Guerreira", gender, 7);
}
