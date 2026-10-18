namespace Avalon.World.Doodads.Scripts;

/// <summary>
/// A chest: its use drops the template's loot table and copper at its feet, for the user and the user's party here, as a
/// kill drops (IDoodadUseContext.DropLoot). Spent by its template's use limit once the use returns. The drop is its only
/// act, so nothing can throw after the loot is on the ground and leave the chest ready to be opened again.
/// </summary>
public sealed class LootDoodadScript : DoodadScript
{
    public override void OnUse(IDoodadUseContext ctx) => ctx.DropLoot();
}
