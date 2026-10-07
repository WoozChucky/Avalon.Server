using Avalon.Common.ValueObjects;
using Avalon.Database.World;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.World.Dialogue;
using Avalon.World.Public.Dialogue;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.Seeding;

/// <summary>Marta Ledgerwell, the town's banker (spec #463), as the World database seeds her.</summary>
public class BankerSeedShould
{
    private static readonly CreatureTemplateId s_marta = new(11);

    [Fact]
    public void Seed_Marta_As_An_Unkillable_Town_Npc_That_Drops_Nothing()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        CreatureTemplate marta = context.CreatureTemplates.AsNoTracking().ToList().Single(t => t.Id == s_marta);

        Assert.Equal("Marta Ledgerwell", marta.Name);
        Assert.True(marta.Invulnerable);
        Assert.Equal("TownNpcScript", marta.ScriptName);
        Assert.Null(marta.LootTableId);
        Assert.Equal(0u, marta.Experience);
    }

    [Fact]
    public void Make_Marta_The_Only_Banker_And_Someone_To_Talk_To()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        var nodes = context.DialogueNodes.AsNoTracking().ToList();
        var options = context.DialogueOptions.AsNoTracking().ToList();

        var actions = new DialogueActions(nodes, options);
        var catalog = new DialogueCatalog(nodes, options, NullLoggerFactory.Instance);

        Assert.True(NpcInteraction.IsBanker(actions, s_marta));
        Assert.True(NpcInteraction.CanInteract(catalog, s_marta));
        foreach (ulong other in new ulong[] { 1, 2, 3 })
            Assert.False(NpcInteraction.IsBanker(actions, new CreatureTemplateId(other)));
    }

    [Fact]
    public void Offer_Open_My_Bank_Then_Farewell_At_Martas_Root()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        var nodes = context.DialogueNodes.AsNoTracking().ToList();
        var options = context.DialogueOptions.AsNoTracking().ToList();
        var catalog = new DialogueCatalog(nodes, options, NullLoggerFactory.Instance);
        var actions = new DialogueActions(nodes, options);

        DialogueNodeView root = catalog.GetRoot(s_marta)!;

        Assert.Equal(2, root.Options.Count);
        DialogueOptionView open = root.Options[0], farewell = root.Options[1];
        Assert.Equal(DialogueOptionAction.OpenBank, actions.For(open.Id));
        Assert.Equal(root.Id, open.NextNodeId);        // back to her greeting: the bank stays open
        Assert.Null(actions.For(farewell.Id));
        Assert.Null(farewell.NextNodeId);
        Assert.Equal(10, farewell.TextId.Value);       // the shared "Farewell."
    }
}
