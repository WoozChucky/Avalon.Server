namespace Avalon.World.Doodads;

/// <summary>
/// What a doodad does beyond its template's data: one subclass per behaviour, named by DoodadTemplate.ScriptName and
/// found by class name in ScriptManager, once, at Load (never hot reloaded). World-side, not the modding API. Its hooks
/// come with the doodad runtime; for now the catalog only resolves the name.
/// </summary>
public abstract class DoodadScript;
