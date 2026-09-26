using Avalon.Common.Accounts;
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
    private static readonly CreatureTemplateId Marta = new(11);

    [Fact]
    public void Seed_Marta_As_An_Unkillable_Town_Npc_That_Drops_Nothing()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        CreatureTemplate marta = context.CreatureTemplates.AsNoTracking().ToList().Single(t => t.Id == Marta);

        Assert.Equal("Marta Ledgerwell", marta.Name);
        Assert.True(marta.Invulnerable);
        Assert.Equal("TownNpcScript", marta.ScriptName);
        Assert.Null(marta.LootTableId);
        Assert.Equal(0u, marta.Experience);
    }

    [Fact]
    public void Place_Marta_On_Map_One()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        MapCreatureSpawn spawn = context.MapCreatureSpawns.AsNoTracking().ToList()
            .Single(s => s.CreatureTemplateId == Marta);

        Assert.Equal(1u, spawn.MapTemplateId.Value);
        Assert.Equal((-6f, 0f, 6f, 135f), (spawn.OffsetX, spawn.OffsetY, spawn.OffsetZ, spawn.Facing));
    }

    [Fact]
    public void Make_Marta_The_Only_Banker_And_Someone_To_Talk_To()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        List<DialogueNode> nodes = context.DialogueNodes.AsNoTracking().ToList();
        List<DialogueOption> options = context.DialogueOptions.AsNoTracking().ToList();

        var actions = new DialogueActions(nodes, options);
        var catalog = new DialogueCatalog(nodes, options, NullLoggerFactory.Instance);

        Assert.True(NpcInteraction.IsBanker(actions, Marta));
        Assert.True(NpcInteraction.CanInteract(catalog, Marta));
        foreach (ulong other in new ulong[] { 1, 2, 3 })
            Assert.False(NpcInteraction.IsBanker(actions, new CreatureTemplateId(other)));
    }

    [Fact]
    public void Offer_Open_My_Bank_Then_Farewell_At_Martas_Root()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        List<DialogueNode> nodes = context.DialogueNodes.AsNoTracking().ToList();
        List<DialogueOption> options = context.DialogueOptions.AsNoTracking().ToList();
        var catalog = new DialogueCatalog(nodes, options, NullLoggerFactory.Instance);
        var actions = new DialogueActions(nodes, options);

        DialogueNodeView root = catalog.GetRoot(Marta)!;

        Assert.Equal(2, root.Options.Count);
        DialogueOptionView open = root.Options[0], farewell = root.Options[1];
        Assert.Equal(DialogueOptionAction.OpenBank, actions.For(open.Id));
        Assert.Equal(root.Id, open.NextNodeId);        // back to her greeting: the bank stays open
        Assert.Null(actions.For(farewell.Id));
        Assert.Null(farewell.NextNodeId);
        Assert.Equal(10, farewell.TextId.Value);       // the shared "Farewell."
    }

    [Fact]
    public void Write_And_Translate_Martas_Lines()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        List<LocalizedText> texts = context.LocalizedTexts.AsNoTracking().ToList();
        List<LocalizedTextLocale> locales = context.LocalizedTextLocales.AsNoTracking().ToList();

        foreach (int id in new[] { 15, 16 })
        {
            Assert.Contains(texts, t => t.Id.Value == id && t.Text.Length > 0);
            Assert.Contains(locales, l => l.TextId.Value == id && l.Locale == AccountLocale.ptPT);
        }
    }
}
