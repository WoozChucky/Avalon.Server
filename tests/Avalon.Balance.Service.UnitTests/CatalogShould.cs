using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avalon.Balance.Contract;
using Avalon.Balance.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Avalon.Balance.Service.UnitTests;

public class CatalogShould
{
    [Fact]
    public async Task Describe_the_tunables_defaults_and_version_for_the_secret_holder()
    {
        await using WebApplication app = BalanceTestHost.Build();
        await app.StartAsync();
        HttpClient client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Balance-Secret", BalanceTestHost.Secret);

        HttpResponseMessage response = await client.GetAsync("/catalog");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CatalogDto? catalog = await response.Content.ReadFromJsonAsync<CatalogDto>(BalanceJson.Options);
        Assert.NotNull(catalog);
        Assert.True(catalog.Tunables.Count >= 100);
        Assert.Contains(catalog.Tunables, t => t.Key == "CombatFormula.CritMultiplier");
        Assert.False(string.IsNullOrWhiteSpace(catalog.Version));
        Assert.False(string.IsNullOrWhiteSpace(catalog.Commit));
        ScenarioFile scenarios = ConfigFiles.ParseScenarios(catalog.DefaultConfig.Scenarios);
        Assert.Equal(scenarios.Scenarios.Select(s => s.Id), catalog.Scenarios);
        Assert.NotEmpty(catalog.Classes);
        Assert.Equal(CheckedInBuildIds(), catalog.Builds.Select(b => b.Id));
        BuildDto bloodPrice = Assert.Single(catalog.Builds, b => b.Id == "warrior-blood-price");
        Assert.Equal("Warrior", bloodPrice.Class);
        Assert.Equal(
        [
            new TalentNodeDto(57, "Small", null),
            new TalentNodeDto(58, "Small", null),
            new TalentNodeDto(59, "Small", null),
            new TalentNodeDto(60, "Small", null),
            new TalentNodeDto(61, "Ability", "Hurled Axe"),
            new TalentNodeDto(62, "Small", null),
            new TalentNodeDto(66, "Small", null),
            new TalentNodeDto(67, "Small", null),
            new TalentNodeDto(68, "Notable", "Brawn"),
            new TalentNodeDto(302, "Junction", null),
            new TalentNodeDto(332, "Keystone", "Blood Price"),
            new TalentNodeDto(2, "Small", null),
            new TalentNodeDto(3, "Small", null),
        ], bloodPrice.Nodes);
        Assert.NotEmpty(catalog.Levels);
        Assert.NotEmpty(catalog.Gear);
        Assert.NotEmpty(catalog.Abilities);
        Assert.NotEmpty(catalog.Creatures);
    }

    /// <summary>The build ids of the checked-in builds.json, in the file's order, read as plain JSON.</summary>
    private static IEnumerable<string> CheckedInBuildIds()
    {
        using var file = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "balance", "builds.json")));
        return file.RootElement.EnumerateObject().Select(p => p.Name).ToList();
    }
}
