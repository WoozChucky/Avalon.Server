using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Localization;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Localization;

public class LocalizedTextCatalogShould
{
    [Fact]
    public void Return_The_Translation_When_One_Exists()
    {
        ILocalizedTextCatalog catalog = Catalog(
            texts: [Text(1, "Farewell.")],
            locales: [Locale(1, AccountLocale.ptPT, "Adeus.")]);

        Assert.Equal("Adeus.", catalog.Get(new LocalizedTextId(1), Context(AccountLocale.ptPT)));
    }

    [Fact]
    public void Fall_Back_To_The_Base_Text_When_The_Locale_Has_No_Row()
    {
        // Partial translation has to be shippable, so a miss is not an error.
        ILocalizedTextCatalog catalog = Catalog(
            texts: [Text(1, "Farewell.")],
            locales: [Locale(1, AccountLocale.ptPT, "Adeus.")]);

        Assert.Equal("Farewell.", catalog.Get(new LocalizedTextId(1), Context(AccountLocale.frFR)));
    }

    [Fact]
    public void Return_Empty_And_Not_Throw_For_An_Unknown_Id()
    {
        // Read from inside the tick loop: missing content must not be able to take a map down.
        ILocalizedTextCatalog catalog = Catalog(texts: [Text(1, "Farewell.")], locales: []);

        Assert.Equal(string.Empty, catalog.Get(new LocalizedTextId(99), Context(AccountLocale.enUS)));
    }

    [Fact]
    public void Interpolate_The_Resolved_Text()
    {
        ILocalizedTextCatalog catalog = Catalog(
            texts: [Text(1, "Hello, {name}.")],
            locales: []);

        Assert.Equal("Hello, Aldric.",
            catalog.Get(new LocalizedTextId(1), Context(AccountLocale.enUS, name: "Aldric")));
    }

    [Fact]
    public void Resolve_A_Class_Name_That_Inflects_On_Gender()
    {
        // The two-pass case, and the one a single-pass implementation gets wrong: it would emit the
        // literal "Guerreir{g:o|a}" to the player. Invisible in English, where no class inflects.
        ILocalizedTextCatalog catalog = Catalog(
            texts: [Text(1, "Warrior")],
            locales: [Locale(1, AccountLocale.ptPT, "Guerreir{g:o|a}")],
            classNames: [new CharacterClassName { Class = CharacterClass.Warrior, TextId = 1 }]);

        ICharacter character = Character("Aldric", CharacterClass.Warrior, CharacterGender.Female, 7);

        TextContext context = catalog.ContextFor(character, AccountLocale.ptPT);

        Assert.Equal("Guerreira", context.PlayerClassName);
    }

    [Fact]
    public void Build_A_Context_Carrying_The_Characters_Own_Details()
    {
        ILocalizedTextCatalog catalog = Catalog(
            texts: [Text(1, "Warrior")],
            locales: [],
            classNames: [new CharacterClassName { Class = CharacterClass.Warrior, TextId = 1 }]);

        ICharacter character = Character("Aldric", CharacterClass.Warrior, CharacterGender.Male, 7);

        TextContext context = catalog.ContextFor(character, AccountLocale.enUS);

        Assert.Equal("Aldric", context.PlayerName);
        Assert.Equal((ushort)7, context.PlayerLevel);
        Assert.Equal(CharacterGender.Male, context.PlayerGender);
        Assert.Equal(AccountLocale.enUS, context.Locale);
    }

    [Fact]
    public void Leave_The_Class_Name_Empty_When_The_Class_Has_No_Row()
    {
        ILocalizedTextCatalog catalog = Catalog(texts: [], locales: [], classNames: []);

        ICharacter character = Character("Aldric", CharacterClass.Hunter, CharacterGender.Male, 1);

        Assert.Equal(string.Empty, catalog.ContextFor(character, AccountLocale.enUS).PlayerClassName);
    }

    [Fact]
    public void Keep_Two_Readers_Resolutions_Separate()
    {
        // Two players of different gender and locale talking to the same NPC on the same tick. The
        // catalog is read-only and the context is per-call, so nothing may leak between them.
        ILocalizedTextCatalog catalog = Catalog(
            texts: [Text(1, "Welcome, {class} {name}.")],
            locales: [Locale(1, AccountLocale.ptPT, "Bem-{g:vindo|vinda}, {class} {name}.")],
            classNames: [new CharacterClassName { Class = CharacterClass.Warrior, TextId = 2 },
                         new CharacterClassName { Class = CharacterClass.Healer, TextId = 3 }]);

        Assert.Equal("Welcome, Warrior Aldric.",
            catalog.Get(new LocalizedTextId(1),
                Context(AccountLocale.enUS, name: "Aldric", className: "Warrior")));

        Assert.Equal("Bem-vinda, Curandeira Mira.",
            catalog.Get(new LocalizedTextId(1),
                Context(AccountLocale.ptPT, name: "Mira", className: "Curandeira",
                    gender: CharacterGender.Female)));
    }

    [Fact]
    public void Warn_Only_Once_For_A_Repeated_Unknown_Id()
    {
        // Get runs on the tick thread for every dialogue read. A bad id on a live NPC must not
        // flood the log for as long as players keep talking to it.
        ILogger innerLogger = Substitute.For<ILogger>();
        ILoggerFactory loggerFactory = Substitute.For<ILoggerFactory>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(innerLogger);

        var catalog = new LocalizedTextCatalog([], [], [], loggerFactory);

        catalog.Get(new LocalizedTextId(99), Context(AccountLocale.enUS));
        catalog.Get(new LocalizedTextId(99), Context(AccountLocale.enUS));
        catalog.Get(new LocalizedTextId(99), Context(AccountLocale.enUS));

        int warnings = innerLogger.ReceivedCalls()
            .Count(call => call.GetMethodInfo().Name == nameof(ILogger.Log)
                && (LogLevel)call.GetArguments()[0]! == LogLevel.Warning);

        Assert.Equal(1, warnings);
    }

    private static LocalizedText Text(int id, string text)
        => new() { Id = new LocalizedTextId(id), Text = text };

    private static LocalizedTextLocale Locale(int id, AccountLocale locale, string text)
        => new() { TextId = new LocalizedTextId(id), Locale = locale, Text = text };

    private static TextContext Context(
        AccountLocale locale,
        string name = "Player",
        string className = "Warrior",
        CharacterGender gender = CharacterGender.Male,
        ushort level = 1)
        => new(locale, name, className, gender, level);

    private static ICharacter Character(string name, CharacterClass cls, CharacterGender gender, ushort level)
    {
        ICharacter character = Substitute.For<ICharacter>();
        character.Name.Returns(name);
        character.Class.Returns(cls);
        character.Gender.Returns(gender);
        character.Level.Returns(level);
        return character;
    }

    private static LocalizedTextCatalog Catalog(
        IReadOnlyCollection<LocalizedText> texts,
        IReadOnlyCollection<LocalizedTextLocale> locales,
        IReadOnlyCollection<CharacterClassName>? classNames = null)
        => new(texts, locales, classNames ?? [], NullLoggerFactory.Instance);
}
