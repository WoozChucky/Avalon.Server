using Avalon.World;
using Avalon.World.Doodads;

namespace Avalon.Server.World.UnitTests.Doodads;

/// <summary>A doodad script the catalog's lookup resolves by name, which does nothing.</summary>
public sealed class TestDoodadScript : DoodadScript
{
    public override void OnUse(IDoodadUseContext ctx)
    {
    }
}

/// <summary>
/// Decides who sees its doodad by <see cref="Rule" />, and counts how often it was asked. Shared by every doodad that
/// names it: tests set both first, and only DoodadReplicationShould, whose rows run one at a time, uses it.
/// </summary>
public sealed class RuledDoodadScript : DoodadScript
{
    public static Func<IDoodadViewContext, bool?> Rule { get; set; } = static _ => null;

    public static int Asked { get; set; }

    public override bool? CanSee(IDoodadViewContext ctx)
    {
        Asked++;
        return Rule(ctx);
    }

    public override void OnUse(IDoodadUseContext ctx)
    {
    }
}

/// <summary>Asks for the world: buildable from the whole container, never through the narrowed one.</summary>
public sealed class WorldHungryDoodadScript(IWorld world) : DoodadScript
{
    public IWorld World { get; } = world;

    public override void OnUse(IDoodadUseContext ctx)
    {
    }
}

/// <summary>
/// Counts its uses and respawns, refuses with <see cref="Refusal" /> when set and throws from OnUse when
/// <see cref="Throws" /> is. Shared by every doodad that names it: only DoodadUseShould uses it, its tests one at a time,
/// each starting from <see cref="Reset" />.
/// </summary>
public sealed class CountingDoodadScript : DoodadScript
{
    public static int Uses { get; private set; }

    public static int Respawns { get; private set; }

    public static string? Refusal { get; set; }

    public static bool Throws { get; set; }

    public static void Reset()
    {
        Uses = 0;
        Respawns = 0;
        Refusal = null;
        Throws = false;
    }

    public override string? CanUse(IDoodadUseContext ctx) => Refusal;

    public override void OnUse(IDoodadUseContext ctx)
    {
        Uses++;
        if (Throws)
            throw new InvalidOperationException("OnUse");
    }

    public override void OnRespawn(IDoodadContext ctx) => Respawns++;
}
