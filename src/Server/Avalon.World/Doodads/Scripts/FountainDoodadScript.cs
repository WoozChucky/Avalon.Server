namespace Avalon.World.Doodads.Scripts;

/// <summary>
/// A fountain: its use restores the template's share of health and power to every living player within the template's
/// radius (IDoodadUseContext.RestoreInRadius). The restore is its only act, so nothing can throw after it.
/// </summary>
public sealed class FountainDoodadScript : DoodadScript
{
    public override void OnUse(IDoodadUseContext ctx) => ctx.RestoreInRadius(ctx.RestorePct, ctx.RestorePct, ctx.AreaRadius);
}
