using Avalon.World;
using Avalon.World.Items;

namespace Avalon.Server.World.UnitTests.ItemUse;

/// <summary>Uses one, and says so on every hook, so a test reads what ran from the lines the player was told.</summary>
public sealed class ConsumeOneScript : ItemScript
{
    public override void OnUse(IItemUseContext ctx)
    {
        ctx.Consume();
        ctx.Tell("used");
    }

    public override void OnCastStart(IItemUseContext ctx) => ctx.Tell("cast started");

    public override void OnInterrupted(IItemUseContext ctx) => ctx.Tell("cast interrupted");
}

public sealed class RefusingScript : ItemScript
{
    public override string? CanUse(IItemUseContext ctx) => "Not now.";

    public override void OnUse(IItemUseContext ctx) => ctx.Tell("used");
}

public sealed class ThrowingScript : ItemScript
{
    public override void OnUse(IItemUseContext ctx)
    {
        ctx.Consume();
        throw new InvalidOperationException("boom");
    }
}

/// <summary>Asks for a service that writes, which the narrowed provider refuses.</summary>
public sealed class WorldHungryItemScript(IWorld world) : ItemScript
{
    public IWorld World { get; } = world;

    public override void OnUse(IItemUseContext ctx)
    {
    }
}

public sealed class SampleItemScript : ItemScript
{
    public override void OnUse(IItemUseContext ctx)
    {
    }
}

/// <summary>A cast-time use whose interruption hook throws: the use is answered InternalError, not Interrupted.</summary>
public sealed class InterruptThrowingScript : ItemScript
{
    public override void OnUse(IItemUseContext ctx) => ctx.Consume();

    public override void OnInterrupted(IItemUseContext ctx) => throw new InvalidOperationException("boom");
}
