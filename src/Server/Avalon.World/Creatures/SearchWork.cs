namespace Avalon.World.Creatures;

/// <summary>
/// Whether the creatures' range searches and the spatial grid's search filter count their work (#982): the counts read by
/// the World tests and the <c>detection-counts</c> harness (<c>CreatureRangeDetectorScript.Searches</c>, the grid's
/// probe counts). Off unless the AppContext switch <see cref="Switch" /> is on when this is first read: the World test
/// project sets it in its runtime configuration, the harness before it builds a world. Read once, into a
/// <c>static readonly</c> the JIT folds, so a server, which never sets it, pays nothing for the counts.
/// </summary>
internal static class SearchWork
{
    public const string Switch = "Avalon.World.CountSearchWork";

    public static readonly bool Counted = AppContext.TryGetSwitch(Switch, out bool counted) && counted;
}
