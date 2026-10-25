namespace Avalon.Domain.World;

/// <summary>
/// The Resonance themes one modifier catalog holds (#1180), built once per catalog load by <see cref="ItemModifierCatalog" />
/// alone. Its theme list is the distinct theme ids its accepted prefix and suffix rows name, sorted ascending; a theme's
/// <b>slot</b> is its position in that list (0 to <see cref="Count" /> - 1), so nothing is indexed by an id's value and no
/// number of themes is too many. A slot belongs to this table only: it is never stored or sent, the theme id is. Every
/// affix modifier maps to its one slot through its group; an implicit or unique modifier has no theme and no entry.
/// Never changed after it is built; every lookup allocates nothing.
/// </summary>
public sealed class ResonanceTable
{
    private readonly Dictionary<int, int> _slotOfModifier;
    private readonly Dictionary<int, int> _slotOfTheme;
    private readonly int[] _themeIds;

    /// <summary>The table with no theme: what a caller without a catalog counts against (character create, tests).</summary>
    public static ResonanceTable Empty { get; } = new(Array.Empty<ItemModifier>());

    internal ResonanceTable(IReadOnlyCollection<ItemModifier> accepted)
    {
        _themeIds = [.. accepted.Where(IsThemedAffix).Select(modifier => modifier.Group.ThemeId!.Value).Distinct().Order()];

        _slotOfTheme = new Dictionary<int, int>(_themeIds.Length);
        for (int slot = 0; slot < _themeIds.Length; slot++)
            _slotOfTheme[_themeIds[slot]] = slot;

        _slotOfModifier = [];
        foreach (ItemModifier modifier in accepted)
        {
            if (IsThemedAffix(modifier))
                _slotOfModifier[modifier.Id] = _slotOfTheme[modifier.Group.ThemeId!.Value];
        }
    }

    /// <summary>How many themes the table holds, which is how many slots a count against it has. No limit.</summary>
    public int Count => _themeIds.Length;

    /// <summary>The theme id of <paramref name="slot" />.</summary>
    public int ThemeIdAt(int slot) => _themeIds[slot];

    /// <summary>The slot of the affix modifier's theme; false for an implicit, a unique or a modifier the table does not hold.</summary>
    public bool TrySlotOfModifier(int modifierId, out int slot) => _slotOfModifier.TryGetValue(modifierId, out slot);

    /// <summary>The slot of the theme; false for a theme no accepted affix names.</summary>
    public bool TrySlotOfTheme(int themeId, out int slot) => _slotOfTheme.TryGetValue(themeId, out slot);

    private static bool IsThemedAffix(ItemModifier modifier) =>
        modifier.Group.Kind is ItemModifierKind.Prefix or ItemModifierKind.Suffix && modifier.Group.ThemeId is not null;
}
