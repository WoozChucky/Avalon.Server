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
        Assert.NotEmpty(catalog.Levels);
        Assert.NotEmpty(catalog.Gear);
        Assert.NotEmpty(catalog.Abilities);
        Assert.NotEmpty(catalog.Creatures);
    }
}
