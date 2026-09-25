using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>
/// One string the server can show a player, in the base locale (enUS). Translations live in
/// <see cref="LocalizedTextLocale"/>; a locale with no row falls back to this text.
/// </summary>
/// <remarks>
/// Deliberately central rather than inline on whatever references it. Both dialogue nodes and
/// dialogue options are translatable, and quests, items and abilities will be — inline text would
/// mean a locale table per structural table, and the count would grow with every new kind of
/// content. One shared text table keeps translation to a single table, whatever references it.
/// </remarks>
public class LocalizedText : IDbEntity<LocalizedTextId>
{
    public LocalizedTextId Id { get; set; } = default!;

    /// <summary>The enUS wording, which is also the fallback for any untranslated locale.</summary>
    public string Text { get; set; } = string.Empty;
}
