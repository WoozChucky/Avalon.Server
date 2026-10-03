using Avalon.World;
using Avalon.World.Auras;

namespace Avalon.Server.World.UnitTests.Auras;

/// <summary>Records every hook it hears, by aura and hook name. Shared by every aura: tests clear it first.</summary>
public sealed class RecordingAuraScript : AuraScript
{
    public static readonly List<string> Heard = [];

    public override void OnApply(IAuraContext ctx) => Heard.Add($"{ctx.AuraName}:apply:{ctx.Stacks}");

    public override void OnTick(IAuraContext ctx) => Heard.Add($"{ctx.AuraName}:tick");

    public override void OnStack(IAuraContext ctx) => Heard.Add($"{ctx.AuraName}:stack:{ctx.Stacks}");

    public override void OnRemove(IAuraContext ctx, AuraRemoveReason reason) => Heard.Add($"{ctx.AuraName}:remove:{reason}");
}

/// <summary>Throws from every hook.</summary>
public sealed class ThrowingAuraScript : AuraScript
{
    public override void OnApply(IAuraContext ctx) => throw new InvalidOperationException("apply");

    public override void OnTick(IAuraContext ctx) => throw new InvalidOperationException("tick");

    public override void OnRemove(IAuraContext ctx, AuraRemoveReason reason) => throw new InvalidOperationException("remove");
}

/// <summary>Ends its aura on its first tick.</summary>
public sealed class EndOnTickAuraScript : AuraScript
{
    public override void OnTick(IAuraContext ctx) => ctx.Remove();
}

/// <summary>Asks for the world: buildable from the whole container, never through the narrowed one.</summary>
public sealed class WorldHungryAuraScript(IWorld world) : AuraScript
{
    public IWorld World { get; } = world;
}

/// <summary>Records its tick, then deals its target all the health it has left; records its removal.</summary>
public sealed class KillOnTickAuraScript : AuraScript
{
    public override void OnTick(IAuraContext ctx)
    {
        RecordingAuraScript.Heard.Add($"{ctx.AuraName}:tick");
        ctx.Damage(ctx.TargetHealth);
    }

    public override void OnRemove(IAuraContext ctx, AuraRemoveReason reason) =>
        RecordingAuraScript.Heard.Add($"{ctx.AuraName}:remove:{reason}");
}

/// <summary>Records its tick and ends its aura from it; records its removal.</summary>
public sealed class EndOnTickRecordingAuraScript : AuraScript
{
    public override void OnTick(IAuraContext ctx)
    {
        RecordingAuraScript.Heard.Add($"{ctx.AuraName}:tick");
        ctx.Remove();
    }

    public override void OnRemove(IAuraContext ctx, AuraRemoveReason reason) =>
        RecordingAuraScript.Heard.Add($"{ctx.AuraName}:remove:{reason}");
}

/// <summary>Ends its aura from the hook that applied it; records its removal.</summary>
public sealed class EndOnApplyAuraScript : AuraScript
{
    public override void OnApply(IAuraContext ctx) => ctx.Remove();

    public override void OnRemove(IAuraContext ctx, AuraRemoveReason reason) =>
        RecordingAuraScript.Heard.Add($"{ctx.AuraName}:remove:{reason}");
}
