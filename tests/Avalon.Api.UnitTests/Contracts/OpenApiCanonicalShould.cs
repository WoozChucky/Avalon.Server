using System.Text.Json.Nodes;
using Xunit;

namespace Avalon.Api.UnitTests.Contracts;

public sealed class OpenApiCanonicalShould
{
    private const string InApi = "Avalon.Api, Version=1.0.0.0, Culture=neutral, PublicKeyToken=d969c60c55bf305e";
    private const string InContract = "Avalon.Api.Contract, Version=1.0.0.0, Culture=neutral, PublicKeyToken=d969c60c55bf305e";

    [Theory]
    [InlineData("Avalon.Database.PagedResult`1[[Avalon.Api.Contract.CharacterDto, " + InApi + "]]",
        "Avalon.Database.PagedResult`1[[Avalon.Api.Contract.CharacterDto, *]]")]
    [InlineData("System.Collections.Generic.Dictionary`2[[System.String, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, "
                + "PublicKeyToken=7cec85d7bea7798e],[System.String[], System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, "
                + "PublicKeyToken=7cec85d7bea7798e]]",
        "System.Collections.Generic.Dictionary`2[[System.String, *],[System.String[], *]]")]
    [InlineData("Outer`1[[Inner`1[[Leaf, Unsigned, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null]], " + InApi + "]]",
        "Outer`1[[Inner`1[[Leaf, *]], *]]")]
    [InlineData("Avalon.Api.Contract.AccountDto", "Avalon.Api.Contract.AccountDto")]
    public void Replace_every_assembly_qualifier_with_a_star(string name, string expected) =>
        Assert.Equal(expected, OpenApiCanonical.WithoutAssemblyQualifiers(name));

    [Fact]
    public void Replace_the_qualifiers_in_schema_keys_and_refs()
    {
        JsonObject canonical = OpenApiCanonical.Canonicalise(Document(InApi));

        string key = "Avalon.Database.PagedResult`1[[Avalon.Api.Contract.CharacterDto, *]]";
        Assert.Equal(["Avalon.Api.Contract.CharacterDto", key], Keys(canonical["components"]!["schemas"]!));
        Assert.Equal("#/components/schemas/" + key,
            canonical["paths"]!["/character"]!["get"]!["responses"]!["200"]!["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>());
        Assert.Equal("#/components/schemas/Avalon.Api.Contract.CharacterDto",
            canonical["components"]!["schemas"]![key]!["properties"]!["items"]!["items"]!["$ref"]!.GetValue<string>());
    }

    [Fact]
    public void Find_no_difference_when_a_type_moves_to_another_assembly() =>
        Assert.Empty(OpenApiCanonical.Differences(
            OpenApiCanonical.Canonicalise(Document(InApi)), OpenApiCanonical.Canonicalise(Document(InContract))));

    [Fact]
    public void Put_paths_schemas_and_tags_in_ordinal_order()
    {
        JsonObject canonical = OpenApiCanonical.Canonicalise(JsonNode.Parse("""
            {
              "paths": { "/world": {}, "/account": {}, "/World": {} },
              "components": { "schemas": { "b": {}, "B": {}, "a": {} } },
              "tags": [ { "name": "World" }, { "name": "Account" }, { "name": "account" } ]
            }
            """)!);

        Assert.Equal(["/World", "/account", "/world"], Keys(canonical["paths"]!));
        Assert.Equal(["B", "a", "b"], Keys(canonical["components"]!["schemas"]!));
        Assert.Equal(["Account", "World", "account"], canonical["tags"]!.AsArray().Select(tag => tag!["name"]!.GetValue<string>()));
    }

    [Fact]
    public void Find_no_difference_when_only_paths_schemas_and_tags_are_reordered() =>
        Assert.Empty(OpenApiCanonical.Differences(
            OpenApiCanonical.Canonicalise(JsonNode.Parse("""
                { "paths": { "/a": {}, "/b": {} }, "components": { "schemas": { "A": {}, "B": {} } }, "tags": [ { "name": "a" }, { "name": "b" } ] }
                """)!),
            OpenApiCanonical.Canonicalise(JsonNode.Parse("""
                { "paths": { "/b": {}, "/a": {} }, "components": { "schemas": { "B": {}, "A": {} } }, "tags": [ { "name": "b" }, { "name": "a" } ] }
                """)!)));

    [Fact]
    public void Drop_the_servers()
    {
        JsonObject canonical = OpenApiCanonical.Canonicalise(JsonNode.Parse("""
            { "openapi": "3.1.1", "servers": [ { "url": "http://localhost/" } ], "paths": {} }
            """)!);

        Assert.Equal(["openapi", "paths"], Keys(canonical));
    }

    [Fact]
    public void Leave_the_document_it_canonicalises_as_it_was()
    {
        JsonNode document = Document(InApi);
        string before = document.ToJsonString();

        OpenApiCanonical.Canonicalise(document);

        Assert.Equal(before, document.ToJsonString());
    }

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

    [Fact]
    public void Report_no_more_than_the_limit()
    {
        var expected = new JsonObject();
        var actual = new JsonObject();
        for (int i = 0; i < 30; i++)
        {
            expected["p" + i] = i;
            actual["p" + i] = -i - 1;
        }

        IReadOnlyList<string> differences = OpenApiCanonical.Differences(expected, actual);

        Assert.Equal(OpenApiCanonical.DefaultLimit, differences.Count);
        Assert.Equal("/p0", differences[0]);
    }

    private static IEnumerable<string> Keys(JsonNode node) => node.AsObject().Select(member => member.Key);

    /// <summary>A document whose generic schema names its type argument's assembly as <paramref name="assembly"/>.</summary>
    private static JsonNode Document(string assembly)
    {
        string paged = $"Avalon.Database.PagedResult`1[[Avalon.Api.Contract.CharacterDto, {assembly}]]";
        return new JsonObject
        {
            ["openapi"] = "3.1.1",
            ["paths"] = new JsonObject
            {
                ["/character"] = new JsonObject
                {
                    ["get"] = new JsonObject
                    {
                        ["responses"] = new JsonObject
                        {
                            ["200"] = new JsonObject
                            {
                                ["content"] = new JsonObject
                                {
                                    ["application/json"] = new JsonObject
                                    {
                                        ["schema"] = new JsonObject { ["$ref"] = "#/components/schemas/" + paged },
                                    },
                                },
                            },
                        },
                    },
                },
            },
            ["components"] = new JsonObject
            {
                ["schemas"] = new JsonObject
                {
                    ["Avalon.Api.Contract.CharacterDto"] = new JsonObject { ["type"] = "object" },
                    [paged] = new JsonObject
                    {
                        ["properties"] = new JsonObject
                        {
                            ["items"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["items"] = new JsonObject { ["$ref"] = "#/components/schemas/Avalon.Api.Contract.CharacterDto" },
                            },
                        },
                    },
                },
            },
        };
    }
}
