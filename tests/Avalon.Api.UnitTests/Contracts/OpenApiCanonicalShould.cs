using System.Text.Json.Nodes;
using Xunit;

namespace Avalon.Api.UnitTests.Contracts;

/// <summary>
/// The comparer <see cref="ContractGoldenShould"/> relies on must not miss a difference: a comparer that found none
/// would let any contract change through silently. A comparer that finds too many fails the golden test loudly.
/// </summary>
public sealed class OpenApiCanonicalShould
{
    [Fact]
    public void Report_each_difference_as_a_json_pointer()
    {
        JsonNode expected = JsonNode.Parse("""
            { "paths": { "/world/{id}": { "get": { "operationId": "GetWorld", "tags": [ "World" ], "deprecated": false } } } }
            """)!;
        JsonNode actual = JsonNode.Parse("""
            { "paths": { "/world/{id}": { "get": { "operationId": "FindWorld", "tags": [ "World", "Extra" ], "summary": "x" } } } }
            """)!;

        Assert.Equal(
            ["/paths/~1world~1{id}/get/operationId", "/paths/~1world~1{id}/get/tags/1", "/paths/~1world~1{id}/get/deprecated",
                "/paths/~1world~1{id}/get/summary"],
            OpenApiCanonical.Differences(expected, actual));
    }

    [Fact]
    public void Report_members_in_another_order_everywhere_else()
    {
        JsonNode expected = JsonNode.Parse("""{ "components": { "schemas": { "A": { "properties": { "id": {}, "name": {} } } } } }""")!;
        JsonNode actual = JsonNode.Parse("""{ "components": { "schemas": { "A": { "properties": { "name": {}, "id": {} } } } } }""")!;

        Assert.Equal(["/components/schemas/A/properties"],
            OpenApiCanonical.Differences(OpenApiCanonical.Canonicalise(expected), OpenApiCanonical.Canonicalise(actual)));
    }
}
