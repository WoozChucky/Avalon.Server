using Avalon.Common.Accounts;
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

    // Read-path misses are warned once per distinct key rather than once per call: Get and
    // ResolveClassName run on the 60 Hz tick thread for every dialogue read, and an id or class
    // referenced by a live NPC would otherwise flood the log for as long as players keep talking
    // to it. Mutated only from that single tick thread (the catalog has no other caller), so no
    // locking — this is not a thread-safety guarantee if that ever changes.
    private readonly HashSet<int> _warnedUnknownIds = [];
    private readonly HashSet<CharacterClass> _warnedUnknownClasses = [];

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

        // Empty rather than a throw: this runs inside the tick loop. Warned once per distinct id.
        if (_warnedUnknownIds.Add(id.Value))
        {
            _logger.LogWarning("No localized text for id {TextId}", id.Value);
        }

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
    /// class-name string containing {class} cannot recurse — the inner context's own class name is
    /// always empty, so {class} resolves to nothing rather than calling back into this method.
    /// That is NOT the same as saying it is caught: the seed test that diffs value tokens across
    /// locales (Keep_The_Same_Value_Tokens_In_Every_Translation) only flags it when base and
    /// translation disagree about whether {class} appears. A class name where every locale,
    /// including the base, contains {class} passes every seed test today and renders an empty
    /// class name in production.
    /// </summary>
    private string ResolveClassName(CharacterClass cls, CharacterGender gender, AccountLocale locale)
    {
        if (!_classNames.TryGetValue(cls, out LocalizedTextId? textId))
        {
            if (_warnedUnknownClasses.Add(cls))
            {
                _logger.LogWarning("No display name for character class {Class}", cls);
            }

            return string.Empty;
        }

        var inner = new TextContext(locale, string.Empty, string.Empty, gender, 0);
        return Get(textId, inner);
    }
}
