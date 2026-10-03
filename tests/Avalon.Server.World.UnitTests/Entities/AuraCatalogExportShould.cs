using System.Text.Json;
using Avalon.Database.World;
using Avalon.Domain.World;
using Avalon.Exporter;
using Avalon.Server.World.UnitTests.Auras;
using Avalon.Server.World.UnitTests.Handlers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Server.World.UnitTests.Entities;

/// <summary>
/// The aura catalog a client names and draws auras from: what each one is called, its icon, kind, timing, stacking and
/// stat modifiers. Rendering is pure and tested here; reading the rows is EF and is not.
/// </summary>
public class AuraCatalogExportShould
{
    private static List<JsonElement> Rows(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("auras").EnumerateArray().Select(r => r.Clone()).ToList();
    }

    [Fact]
    public void List_every_aura_in_id_order_with_what_a_client_draws()
    {
        List<JsonElement> rows = Rows(AuraCatalogExport.Render([AuraTestData.Fortified(), AuraTestData.Bleed()]));

        Assert.Equal([901L, 905L], rows.Select(r => r.GetProperty("id").GetInt64()));
        JsonElement bleed = rows[0];
        Assert.Equal(("Bleed", "bleed", 2, 12000, 3000, 1, 2, 3), (bleed.GetProperty("name").GetString(), bleed.GetProperty("icon").GetString(),
            bleed.GetProperty("kind").GetInt32(), bleed.GetProperty("durationMs").GetInt32(), bleed.GetProperty("tickIntervalMs").GetInt32(),
            bleed.GetProperty("periodicKind").GetInt32(), bleed.GetProperty("stacking").GetInt32(), bleed.GetProperty("maxStacks").GetInt32()));
        JsonElement armour = Assert.Single(rows[1].GetProperty("modifiers").EnumerateArray());
        Assert.Equal((1, 2, 20f), (armour.GetProperty("stat").GetInt32(), armour.GetProperty("kind").GetInt32(),
            armour.GetProperty("value").GetSingle()));
    }

    /// <summary>Server-only balance (the base, the scaling, the script) stays out of a client file.</summary>
    [Fact]
    public void Leave_the_server_only_balance_out() =>
        Assert.Equal(
            ["id", "name", "icon", "kind", "durationMs", "tickIntervalMs", "periodicKind", "stacking", "maxStacks", "modifiers"],
            Rows(AuraCatalogExport.Render([AuraTestData.Bleed()]))[0].EnumerateObject().Select(p => p.Name));

    [Fact]
    public void Render_an_empty_catalog_as_an_empty_array() => Assert.Empty(Rows(AuraCatalogExport.Render([])));

    /// <summary>The committed file is the seed rendered: a seed change without a re-export (aura-catalog) fails here.</summary>
    [Fact]
    public void Match_the_committed_catalog()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        List<AuraTemplate> seeded = context.AuraTemplates.AsNoTracking().Include(a => a.Modifiers).ToList();

        string committed = File.ReadAllText(Path.Combine(RepositoryRoot(), "schema", AuraCatalogExport.DirectoryName,
            AuraCatalogExport.FileName));

        Assert.Equal(committed.Replace("\r\n", "\n", StringComparison.Ordinal),
            AuraCatalogExport.Render(seeded).Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Avalon.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("No Avalon.sln above the test output.");
    }
}
