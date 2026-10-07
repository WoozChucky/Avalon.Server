using System.Text.Json;
using Avalon.Database.World;
using Avalon.Domain.World;
using Avalon.Exporter;
using Avalon.Server.World.UnitTests.Handlers;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Server.World.UnitTests.Entities;

/// <summary>
/// #163: the ability catalog the client names and draws abilities from, creature abilities included. Rendering
/// is pure and tested here over the seeded rows; reading them is EF and is not.
/// </summary>
public class AbilityCatalogExportShould
{
    private static List<AbilityTemplate> Seeded()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        return context.AbilityTemplates.AsNoTracking().ToList();
    }

    private static List<JsonElement> Rows(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("abilities").EnumerateArray().Select(r => r.Clone()).ToList();
    }

    [Fact]
    public void List_every_ability_in_id_order_the_creatures_included()
    {
        List<AbilityTemplate> seeded = Seeded();

        List<JsonElement> rows = Rows(AbilityCatalogExport.Render(seeded));

        Assert.Equal(seeded.Select(a => (long)a.Id.Value).Order(), rows.Select(r => r.GetProperty("id").GetInt64()));
        Assert.Contains(rows, r => r.GetProperty("id").GetInt64() == 300);
        Assert.Contains(rows, r => r.GetProperty("id").GetInt64() == 200);
    }

    /// <summary>Howling Roar: a 1 s wind-up, a 5 m circle on the creature; every field a client draws it from.</summary>
    [Fact]
    public void Carry_what_a_client_draws_a_wind_up_from()
    {
        JsonElement roar = Rows(AbilityCatalogExport.Render(Seeded())).Single(r => r.GetProperty("id").GetInt64() == 310);

        Assert.Equal("Howling Roar", roar.GetProperty("name").GetString());
        Assert.Equal(0, roar.GetProperty("shape").GetInt32());       // circle
        Assert.Equal(0, roar.GetProperty("aimMode").GetInt32());     // movement
        Assert.Equal(0, roar.GetProperty("anchor").GetInt32());      // on the caster
        Assert.Equal(0, roar.GetProperty("affects").GetInt32());     // hostile
        Assert.Equal(5f, roar.GetProperty("radius").GetSingle());
        Assert.Equal(0f, roar.GetProperty("reach").GetSingle());
        Assert.Equal(1000, roar.GetProperty("castTimeMs").GetInt32());
        Assert.Equal(15000, roar.GetProperty("cooldownMs").GetInt32());
    }

    [Fact]
    public void Carry_a_cones_arc_and_a_projectiles_speed_and_pierce()
    {
        List<JsonElement> rows = Rows(AbilityCatalogExport.Render(Seeded()));
        JsonElement earthsplitter = rows.Single(r => r.GetProperty("id").GetInt64() == 312);
        JsonElement volley = rows.Single(r => r.GetProperty("id").GetInt64() == 313);

        Assert.Equal((1, 5f, 60f), (earthsplitter.GetProperty("shape").GetInt32(),
            earthsplitter.GetProperty("reach").GetSingle(), earthsplitter.GetProperty("arcDegrees").GetSingle()));
        Assert.Equal((2, 1, 12f, 16f, true), (volley.GetProperty("shape").GetInt32(), volley.GetProperty("aimMode").GetInt32(),
            volley.GetProperty("reach").GetSingle(), volley.GetProperty("projectileSpeed").GetSingle(),
            volley.GetProperty("pierce").GetBoolean()));
    }

    /// <summary>Server-only balance (coefficients, threat, costs, the script) stays out of a client file.</summary>
    [Fact]
    public void Leave_the_server_only_balance_out()
    {
        JsonElement bite = Rows(AbilityCatalogExport.Render(Seeded())).Single(r => r.GetProperty("id").GetInt64() == 302);

        Assert.Equal(
            ["id", "name", "shape", "aimMode", "anchor", "affects", "radius", "reach", "arcDegrees", "projectileSpeed",
             "pierce", "castTimeMs", "cooldownMs", "auraId"],
            bite.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void Render_an_empty_catalog_as_an_empty_array() =>
        Assert.Empty(Rows(AbilityCatalogExport.Render([])));
}
