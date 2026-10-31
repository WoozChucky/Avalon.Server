using System.Net;
using System.Net.Http.Json;
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
        Assert.Equal(scenarios.Builds, catalog.Builds.Select(b => b.Id));
        BuildDto bloodPrice = Assert.Single(catalog.Builds, b => b.Id == "warrior-blood-price");
        Assert.Equal("Warrior", bloodPrice.Class);
        Assert.Equal(
        [
            new BuildNodeDto(57, "Small", null),
            new BuildNodeDto(58, "Small", null),
            new BuildNodeDto(59, "Small", null),
            new BuildNodeDto(60, "Small", null),
            new BuildNodeDto(61, "Ability", "Hurled Axe"),
            new BuildNodeDto(62, "Small", null),
            new BuildNodeDto(66, "Small", null),
            new BuildNodeDto(67, "Small", null),
            new BuildNodeDto(68, "Notable", "Brawn"),
            new BuildNodeDto(302, "Junction", null),
            new BuildNodeDto(332, "Keystone", "Blood Price"),
            new BuildNodeDto(2, "Small", null),
            new BuildNodeDto(3, "Small", null),
        ], bloodPrice.Nodes);
        Assert.NotEmpty(catalog.Levels);
        Assert.NotEmpty(catalog.Gear);
        Assert.NotEmpty(catalog.Abilities);
        Assert.NotEmpty(catalog.Creatures);
    }
}
