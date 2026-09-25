using Avalon.Common.ValueObjects;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;

namespace Avalon.World.Public.Localization;

/// <summary>
/// Every localised string the server can show, resolved from memory. Loaded once at startup — this
/// is read from the tick thread, where a database round trip would stall the world.
/// </summary>
public interface ILocalizedTextCatalog
{
    /// <summary>
    /// The string in the reader's locale with tokens substituted, falling back to the base wording
    /// when there is no translation. Returns the empty string for an unknown id.
    /// </summary>
    string Get(LocalizedTextId id, in TextContext context);

    /// <summary>
    /// A context for this reader, with the class name already localised and gender-resolved — the
    /// first of interpolation's two passes.
    /// </summary>
    TextContext ContextFor(ICharacter character, AccountLocale locale);
}
