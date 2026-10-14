using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Reload;

namespace Avalon.World.Maps;

/// <summary>
/// The world's map templates by id (#883): what <see cref="AvalonMapManager" /> holds them in, and the way in for a
/// reader that wants one. A portal entry, a return to town, a teleport and a select each looked their template up with
/// <c>MapTemplates.FirstOrDefault</c> and a closure, and the ones that converted the id inside the closure made a
/// <see cref="MapTemplateId" /> for every template walked.
/// </summary>
/// <remarks>
/// The templates are read once, as the world loads, and never replaced, so the rows and their index
/// (<see cref="KeyedRows{TKey,TRow}" />, as the item templates and the level rows have) are one object for the
/// world's life. The first template of an id answers for it and one without an id is never found, as the walk
/// answered.
/// </remarks>
public static class MapTemplateRows
{
    /// <summary><paramref name="templates" /> with their lookup by id; themselves when they already carry one.</summary>
    public static KeyedRows<ushort, MapTemplate> Keyed(IReadOnlyCollection<MapTemplate> templates) =>
        KeyedRows<ushort, MapTemplate>.Over(templates, static template => template.Id?.Value);

    /// <summary>The template of that id, or null.</summary>
    public static MapTemplate? FindById(this IReadOnlyList<MapTemplate> templates, MapTemplateId id) =>
        id is null ? null : templates.FindById(id.Value);

    /// <summary>
    /// The template of that id, or null: by key over the map manager's rows, and by a plain walk, which allocates
    /// nothing either, over any other list (a world that is not the server's: a test's, a benchmark's).
    /// </summary>
    public static MapTemplate? FindById(this IReadOnlyList<MapTemplate> templates, ushort id)
    {
        if (templates is KeyedRows<ushort, MapTemplate> keyed)
        {
            return keyed.Find(id);
        }

        for (int i = 0; i < templates.Count; i++)
        {
            MapTemplate template = templates[i];
            if (template.Id is { } templateId && templateId.Value == id)
            {
                return template;
            }
        }

        return null;
    }
}
