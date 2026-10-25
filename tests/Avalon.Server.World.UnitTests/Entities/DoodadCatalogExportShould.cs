using System.Text.Json;
using Avalon.Common.ValueObjects;
using Avalon.Database.World;
using Avalon.Domain.World;
using Avalon.Exporter;
using Avalon.Server.World.UnitTests.Handlers;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Server.World.UnitTests.Entities;

/// <summary>
/// The doodad catalog a client draws doodads from: the model, the clips, the interaction and whether it is solid.
/// Rendering is pure and tested here; reading the rows is EF and is not.
/// </summary>
public class DoodadCatalogExportShould
{
    /// <summary>
    /// Nothing sent to a client reveals what a use does: no gate, loot, gold, restore, radius or aura outcome is in a
    /// row, so a shrine's gamble cannot be read from the file.
    /// </summary>
    [Fact]
    public void List_only_what_a_client_draws()
    {
        var shrine = new DoodadTemplate
        {
            Id = new DoodadTemplateId(7),
            Name = "Shrine",
            ModelKit = "props-kit",
            ModelKey = "Shrine",
            Interaction = DoodadInteraction.Click,
            InteractionRange = 5f,
            AreaRadius = 10f,
            UseLimit = DoodadUseLimit.OncePerInstance,
            SpentLook = DoodadSpentLook.ShowUsed,
            Collision = DoodadCollision.Box,
            CollisionHalfX = 0.6f,
            CollisionHalfZ = 0.6f,
            CollisionHeight = 1.5f,
            UsableQuestId = 1,
            UsableCondition = DoodadGateCondition.Completed,
            AuraOutcomes = [new DoodadAuraOutcome { DoodadTemplateId = new DoodadTemplateId(7), AuraId = new AuraId(1), Weight = 5 }],
        };

        using var document = JsonDocument.Parse(DoodadCatalogExport.Render([shrine]));
        JsonElement row = Assert.Single(document.RootElement.GetProperty("doodads").EnumerateArray());

        Assert.Equal(
            ["id", "name", "modelKit", "modelKey", "usedModelKey", "readyClip", "useClip", "usedClip", "interaction",
                "interactionTimeMs", "interactionRange", "solid", "spentLook", "service"],
            row.EnumerateObject().Select(p => p.Name));
        Assert.True(row.GetProperty("solid").GetBoolean());
    }

    /// <summary>The committed file is the seed rendered: a seed change without a re-export (doodad-catalog) fails here.</summary>
    [Fact]
    public void Match_the_committed_catalog()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        var seeded = context.DoodadTemplates.AsNoTracking().ToList();

        string committed = File.ReadAllText(Path.Combine(RepositoryRoot(), "schema", DoodadCatalogExport.DirectoryName,
            DoodadCatalogExport.FileName));

        Assert.Equal(committed.Replace("\r\n", "\n", StringComparison.Ordinal),
            DoodadCatalogExport.Render(seeded).Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Avalon.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("No Avalon.sln above the test output.");
    }
}
