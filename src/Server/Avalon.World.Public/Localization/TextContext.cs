using Avalon.World.Public.Enums;

namespace Avalon.World.Public.Localization;

/// <summary>
/// Everything text interpolation can reference. A struct so resolving a node and all its options
/// allocates nothing. Extended, not replaced, when item and quest names become tokens.
/// </summary>
/// <param name="Locale">Used by the catalog to choose a translation, not by the interpolator.</param>
/// <param name="PlayerClassName">
/// The class's display name, ALREADY localised and gender-resolved. The interpolator does not
/// resolve it — the catalog does, in a first pass, because a class name is itself a localised
/// string that may contain its own gender select (Guerreir{g:o|a}).
/// </param>
public readonly record struct TextContext(
    AccountLocale Locale,
    string PlayerName,
    string PlayerClassName,
    CharacterGender PlayerGender,
    ushort PlayerLevel);
