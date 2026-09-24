using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Localization;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Localization;

public class LocalizedTextCatalog : ILocalizedTextCatalog
{
    private readonly Dictionary<int, string> _base;
    private readonly Dictionary<(int TextId, AccountLocale Locale), string> _translations;
    private readonly Dictionary<CharacterClass, LocalizedTextId> _classNames;
    private readonly ILogger<LocalizedTextCatalog> _logger;

    public LocalizedTextCatalog(
        IReadOnlyCollection<LocalizedText> texts,
        IReadOnlyCollection<LocalizedTextLocale> locales,
        IReadOnlyCollection<CharacterClassName> classNames,
        ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<LocalizedTextCatalog>();

        _base = texts.ToDictionary(t => t.Id.Value, t => t.Text);
        _translations = locales.ToDictionary(l => (l.TextId.Value, l.Locale), l => l.Text);
        _classNames = classNames.ToDictionary(n => n.Class, n => n.TextId);

        // Dangling references are reported once, here, rather than per lookup on the tick thread.
        foreach (var locale in locales.Where(l => !_base.ContainsKey(l.TextId.Value)))
        {
            _logger.LogWarning("Translation for text {TextId} ({Locale}) has no base string",
                locale.TextId.Value, locale.Locale);
        }
    }

    public string Get(LocalizedTextId id, in TextContext context)
    {
        if (_translations.TryGetValue((id.Value, context.Locale), out string? translated))
        {
            return TextInterpolator.Resolve(translated, context);
        }

        if (_base.TryGetValue(id.Value, out string? baseText))
        {
            return TextInterpolator.Resolve(baseText, context);
        }

        // Empty rather than a throw: this runs inside the tick loop.
        _logger.LogWarning("No localized text for id {TextId}", id.Value);
        return string.Empty;
    }

    public TextContext ContextFor(ICharacter character, AccountLocale locale)
    {
        return new TextContext(
            locale,
            character.Name,
            ResolveClassName(character.Class, character.Gender, locale),
            character.Gender,
            character.Level);
    }

    /// <summary>
    /// Interpolation's first pass. A class name is itself a localised string, and in Portuguese it
    /// carries its own gender select — Guerreir{g:o|a}. It is resolved here, with a context whose
    /// own class name is empty, so that the result can be substituted into the outer template. A
    /// class-name string containing {class} is a seed error, caught by a test, not a recursion.
    /// </summary>
    private string ResolveClassName(CharacterClass cls, CharacterGender gender, AccountLocale locale)
    {
        if (!_classNames.TryGetValue(cls, out LocalizedTextId? textId))
        {
            _logger.LogWarning("No display name for character class {Class}", cls);
            return string.Empty;
        }

        var inner = new TextContext(locale, string.Empty, string.Empty, gender, 0);
        return Get(textId, inner);
    }
}
