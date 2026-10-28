using Avalon.Common.ValueObjects;
using Avalon.Database.World;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Handlers;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Server.World.UnitTests.Doodads;

/// <summary>The doodad tables over SQLite: a template reads back whole, and the database refuses what the world could not run.</summary>
public class DoodadSchemaShould
{
    private static DoodadTemplate Chest(uint id = 900) => new()
    {
        Id = new DoodadTemplateId(id),
        Name = "Test chest",
        ModelKit = "props-kit",
        ModelKey = "Chest_Wood",
        UseClip = "Chest_Open",
        UsedClip = "Chest_Opened",
        Interaction = DoodadInteraction.Click,
        InteractionTimeMs = 1500,
        InteractionRange = 5f,
        UseLimit = DoodadUseLimit.OncePerInstance,
        SpentLook = DoodadSpentLook.ShowUsed,
        LootTableId = 1,
        MinGold = 5,
        MaxGold = 20,
        Collision = DoodadCollision.None,
        UsableQuestId = 1,
        UsableCondition = DoodadGateCondition.OnObjective,
        UsableObjectiveId = 101,
        AuraOutcomes = [new DoodadAuraOutcome { DoodadTemplateId = new DoodadTemplateId(id), AuraId = new AuraId(4), Weight = 3 }],
    };

    [Fact]
    public async Task Read_a_template_back_whole_with_its_aura_outcomes()
    {
        using var database = SqliteDatabase.World();
        await using (WorldDbContext context = database.CreateDbContext())
        {
            DoodadTemplate chest = Chest();
            chest.Service = DoodadService.Workbench;
            context.DoodadTemplates.Add(chest);
            await context.SaveChangesAsync();
        }

        DoodadTemplate read = Assert.Single(await new DoodadTemplateRepository(database).GetAllAsync(CancellationToken.None),
            t => t.Id.Value == 900);

        Assert.Equal(("Chest_Wood", "Chest_Opened", 1500u), (read.ModelKey, read.UsedClip, read.InteractionTimeMs));
        Assert.Equal(1, read.LootTableId!.Value);
        Assert.Equal((1u, DoodadGateCondition.OnObjective, 101u), (read.UsableQuestId, read.UsableCondition, read.UsableObjectiveId));
        Assert.Null(read.VisibleCondition);
        Assert.Equal(DoodadService.Workbench, read.Service);
        DoodadAuraOutcome outcome = Assert.Single(read.AuraOutcomes);
        Assert.Equal((4u, 3u), (outcome.AuraId.Value, outcome.Weight));
    }

    [Fact]
    public async Task Accept_a_box_exactly_1_05_m_tall()
    {
        using var database = SqliteDatabase.World();
        await using WorldDbContext context = database.CreateDbContext();
        DoodadTemplate template = Chest();
        (template.Collision, template.CollisionHalfX, template.CollisionHalfZ, template.CollisionHeight) = (DoodadCollision.Box, 0.5f, 0.5f, 1.05f);
        context.DoodadTemplates.Add(template);

        await context.SaveChangesAsync();
    }

    [Theory]
    [InlineData("interaction 2")]
    [InlineData("use limit 0")]
    [InlineData("spent look 3")]
    [InlineData("service 3")]
    [InlineData("box height 1.0")]
    [InlineData("box half x 0")]
    [InlineData("min gold above max")]
    [InlineData("restore 101")]
    [InlineData("range 0")]
    [InlineData("visible condition without quest")]
    [InlineData("outcome weight 0")]
    public async Task Refuse_a_row_the_check_constraints_forbid(string mutation)
    {
        using var database = SqliteDatabase.World();
        await using WorldDbContext context = database.CreateDbContext();
        DoodadTemplate template = Chest();
        string constraint = Break(template, mutation);
        context.DoodadTemplates.Add(template);

        DbUpdateException refused = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Contains($"CHECK constraint failed: {constraint}", refused.InnerException!.Message, StringComparison.Ordinal);
    }

    /// <summary>Breaks the row as the case names, and returns the constraint that must refuse it.</summary>
    private static string Break(DoodadTemplate t, string mutation)
    {
        switch (mutation)
        {
            case "interaction 2":
                t.Interaction = (DoodadInteraction)2;
                return "CK_DoodadTemplates_Interaction";
            case "use limit 0":
                t.UseLimit = 0;
                return "CK_DoodadTemplates_UseLimit";
            case "spent look 3":
                t.SpentLook = (DoodadSpentLook)3;
                return "CK_DoodadTemplates_SpentLook";
            case "service 3":
                t.Service = (DoodadService)3;
                return "CK_DoodadTemplates_Service";
            case "box height 1.0":
                (t.Collision, t.CollisionHalfX, t.CollisionHalfZ, t.CollisionHeight) = (DoodadCollision.Box, 0.5f, 0.5f, 1.0f);
                return "CK_DoodadTemplates_Collision";
            case "box half x 0":
                (t.Collision, t.CollisionHalfX, t.CollisionHalfZ, t.CollisionHeight) = (DoodadCollision.Box, 0f, 0.5f, 2f);
                return "CK_DoodadTemplates_Collision";
            case "min gold above max":
                (t.MinGold, t.MaxGold) = (30, 20);
                return "CK_DoodadTemplates_Gold";
            case "restore 101":
                t.RestorePct = 101;
                return "CK_DoodadTemplates_RestorePct";
            case "range 0":
                t.InteractionRange = 0f;
                return "CK_DoodadTemplates_Range";
            case "visible condition without quest":
                t.VisibleCondition = DoodadGateCondition.Completed;
                return "CK_DoodadTemplates_VisibleGate";
            case "outcome weight 0":
                t.AuraOutcomes[0].Weight = 0;
                return "CK_DoodadAuraOutcomes_Weight";
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, "No such case.");
        }
    }
}
