namespace Avalon.World.Doodads.Scripts;

/// <summary>
/// A shrine: a gamble. Its use draws one of the template's aura outcomes by weight, at the moment of use, and applies it
/// to every living player within the template's radius (IDoodadUseContext.ApplyAuraInRadius). Nothing sent before the use
/// names the outcome: it does not exist until the draw. The application is its last act, so nothing can throw after it.
/// </summary>
public sealed class ShrineDoodadScript : DoodadScript
{
    public override void OnUse(IDoodadUseContext ctx)
    {
        IReadOnlyList<DoodadOutcomeView> outcomes = ctx.AuraOutcomes;
        uint total = ctx.OutcomeWeightTotal;
        if (outcomes.Count == 0 || total == 0)
            return;

        // A number in [0, total): the outcome is the first whose running weight passes it.
        long roll = ctx.NextRandom(total);
        for (int i = 0; i < outcomes.Count; i++)
        {
            roll -= outcomes[i].Weight;
            if (roll < 0)
            {
                ctx.ApplyAuraInRadius(outcomes[i].AuraId, ctx.AreaRadius);
                return;
            }
        }
    }
}
