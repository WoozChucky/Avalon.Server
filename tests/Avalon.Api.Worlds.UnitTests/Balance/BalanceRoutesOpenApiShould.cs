using System.Text.Json;
using Avalon.Api.Worlds.UnitTests.Worlds;
using Xunit;

namespace Avalon.Api.Worlds.UnitTests.Balance;

/// <summary>The admin balance routes the Dashboard's generated client depends on.</summary>
public sealed class BalanceRoutesOpenApiShould(OpenApiDocumentFixture fixture) : IClassFixture<OpenApiDocumentFixture>
{
    [Theory]
    [InlineData("/balance/catalog", "get", "GetBalanceCatalog")]
    [InlineData("/balance/runs", "post", "StartBalanceRun")]
    [InlineData("/balance/runs/{id}", "get", "GetBalanceRun")]
    [InlineData("/balance/runs/{id}", "delete", "CancelBalanceRun")]
    [InlineData("/balance/exports", "post", "ExportBalanceChanges")]
    public void Describe_the_balance_route(string path, string verb, string operationId)
    {
        JsonElement paths = fixture.Document.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty(path, out JsonElement item), $"{path} missing");
        Assert.Equal(operationId, item.GetProperty(verb).GetProperty("operationId").GetString());
    }
}
