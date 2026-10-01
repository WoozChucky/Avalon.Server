namespace Avalon.Api.Contract;

/// <summary>
/// The script names one world accepts, for the admin app's dropdowns: <see cref="Ai"/> for a creature template,
/// <see cref="Ability"/> for an ability template, <see cref="Quest"/> for a quest.
/// </summary>
public class WorldScriptCatalogDto
{
    public List<string> Ai { get; set; } = [];
    public List<string> Ability { get; set; } = [];
    public List<string> Quest { get; set; } = [];

    /// <summary>
    /// False when the world has not published its names (no build of it has reported in yet): the lists are then
    /// empty because nothing is known, not because the world has no scripts, and saves are not checked against them.
    /// </summary>
    public bool Published { get; set; }
}
