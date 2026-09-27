using System.Text.Json;
using Xunit;

namespace Avalon.Api.UnitTests.Worlds;

/// <summary>
/// #598: creatures do not respawn, but the creature-template DTO still returns the stored
/// <c>respawnTimerSecs</c> so the Dashboard's generated client keeps compiling. The OpenAPI document,
/// which that client is generated from, has to say the value is unused.
/// </summary>
public sealed class CreatureTemplateOpenApiShould(OpenApiDocumentFixture fixture) : IClassFixture<OpenApiDocumentFixture>
{
    [Fact]
    public void Describe_respawnTimerSecs_as_unused()
    {
        JsonElement schemas = fixture.Document.RootElement.GetProperty("components").GetProperty("schemas");
        JsonElement property = schemas.GetProperty("Avalon.Api.Contract.CreatureTemplateDto")
            .GetProperty("properties").GetProperty("respawnTimerSecs");

        Assert.True(property.TryGetProperty("description", out JsonElement description),
            "respawnTimerSecs has no description in the OpenAPI document");
        Assert.Contains("not used", description.GetString(), StringComparison.Ordinal);

        // Still an int32, so the Dashboard's generated client keeps its type: every int in the document
        // is a $ref to the shared System.Int32 schema, which also accepts a numeric string.
        Assert.Equal("#/components/schemas/System.Int32", property.GetProperty("$ref").GetString());
        JsonElement int32 = schemas.GetProperty("System.Int32");
        Assert.Contains("integer", int32.GetProperty("type").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal("int32", int32.GetProperty("format").GetString());
    }
}
