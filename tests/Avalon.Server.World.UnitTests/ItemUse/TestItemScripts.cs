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

/// <summary>Its OnUse throws after marking one spent: nothing is consumed and no cooldown starts.</summary>
public sealed class OnUseThrowingScript : ItemScript
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

/// <summary>A cast-time use whose start hook throws: the bar ends and the use is answered InternalError, once.</summary>
public sealed class CastStartThrowingScript : ItemScript
{
    public override void OnUse(IItemUseContext ctx) => ctx.Consume();

    public override void OnCastStart(IItemUseContext ctx) => throw new InvalidOperationException("boom");
}

/// <summary>Refused at full health, so a heal landing during its cast refuses it at completion.</summary>
public sealed class RefuseAtFullHealthScript : ItemScript
{
    public override string? CanUse(IItemUseContext ctx) => ctx.Health >= ctx.MaxHealth ? "Already full." : null;

    public override void OnUse(IItemUseContext ctx)
    {
        ctx.Consume();
        ctx.Tell("used");
    }
}
