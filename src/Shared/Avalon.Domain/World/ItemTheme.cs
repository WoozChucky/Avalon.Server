namespace Avalon.Domain.World;

/// <summary>
/// A Resonance theme (#1180): what every prefix and suffix family carries one of, through its group
/// (<see cref="ItemModifierGroup.ThemeId" />). A character counts, per theme, the worn items with an affix of it. A
/// balance table, seeded from ItemModifierSeed.Themes; the id is a plain surrogate.
/// </summary>
public class ItemTheme
{
    public int Id { get; set; }

    /// <summary>The theme's name ("Ember", "Veil").</summary>
    public string Name { get; set; } = null!;
}
