using Avalon.Domain.World;

namespace Avalon.Combat.UnitTests;

/// <summary>Worn items for the stats tests (#1179): each template as a copy that rolled no modifiers.</summary>
internal static class Worn
{
    public static IReadOnlyList<WornItem> Of(params ItemTemplate[] templates)
    {
        var worn = new WornItem[templates.Length];
        for (int i = 0; i < templates.Length; i++) worn[i] = new WornItem(templates[i], []);
        return worn;
    }
}
