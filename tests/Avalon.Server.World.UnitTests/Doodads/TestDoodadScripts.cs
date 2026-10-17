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
