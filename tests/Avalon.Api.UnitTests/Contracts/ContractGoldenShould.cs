using System.Text.Json.Nodes;
using Xunit;

namespace Avalon.Api.UnitTests.Contracts;

/// <summary>
/// The contract does not change while Avalon.Api is split into services (#794): the OpenAPI document the api serves
/// equals the one published before the split, once both are canonical (<see cref="OpenApiCanonical"/>). The golden,
/// <c>openapi.pre-split.json</c>, is the docs build's document generated on main at 54dd4a4a
/// (<c>AVALON_OPENAPI_GENERATION_ONLY=true dotnet build src/Server/Avalon.Api -c Release
/// -p:OpenApiGenerateDocuments=true</c>), byte for byte the published one. This test and the golden go once the
/// rollout is done.
/// </summary>
public sealed class ContractGoldenShould
{
    private const string Golden = "tests/Avalon.Api.UnitTests/Contracts/openapi.pre-split.json";

    [Fact]
    public async Task Serve_the_contract_published_before_the_split()
    {
        JsonNode published = JsonNode.Parse(await File.ReadAllTextAsync(RepositoryRoot.PathOf(Golden)))!;
        await using ContractHost host = await ContractHost.StartAsync();
        JsonNode served = await host.DocumentAsync();

        IReadOnlyList<string> differences = OpenApiCanonical.Differences(
            OpenApiCanonical.Canonicalise(published), OpenApiCanonical.Canonicalise(served));

        Assert.True(differences.Count == 0,
            $"The served OpenAPI document differs from {Golden} at these JSON pointers (the first "
            + $"{OpenApiCanonical.DefaultLimit} at most); the contract does not change during the split (#794):"
            + Environment.NewLine + string.Join(Environment.NewLine, differences));
    }
}
