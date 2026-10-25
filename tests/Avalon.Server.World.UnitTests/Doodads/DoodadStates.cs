using Avalon.World.Doodads;

namespace Avalon.Server.World.UnitTests.Doodads;

/// <summary>
/// A doodad's state written as the tick writes it, without a use. The setter is internal to Avalon.World, which grants
/// this assembly no access, and a test that needs a state no use reaches (a Resonance Workbench despawned, #1179) has no
/// other way to write it.
/// </summary>
internal static class DoodadStates
{
    public static void Set(Doodad doodad, DoodadState state) =>
        (typeof(Doodad).GetProperty(nameof(Doodad.State))?.SetMethod
         ?? throw new MissingMethodException(nameof(Doodad), "set_State")).Invoke(doodad, [state]);
}
