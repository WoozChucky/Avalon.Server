using System.Text.Json;
using Xunit;

namespace Avalon.Api.Worlds.UnitTests.Worlds;

/// <summary>
/// #674: an inventory item whose template has no slot (a potion, a scroll) reports no slot type, so the
/// OpenAPI document the Dashboard's client is generated from has to allow null there.
/// </summary>
public sealed class CharacterInventoryOpenApiShould(OpenApiDocumentFixture fixture) : IClassFixture<OpenApiDocumentFixture>
{
    [Fact]
    public void Describe_the_item_templates_slotType_as_nullable()
    {
        JsonElement schemas = fixture.Document.RootElement.GetProperty("components").GetProperty("schemas");
        JsonElement property = schemas.GetProperty("Avalon.Api.Contract.CharacterInventoryItemTemplateDto")
            .GetProperty("properties").GetProperty("slotType");

        // The same shape as ItemTemplateDto.slot: null, or a reference to the slot-type enum.
        var options = property.GetProperty("oneOf").EnumerateArray().ToList();
        Assert.Contains(options, o => o.TryGetProperty("type", out JsonElement type) && type.GetString() == "null");

        string reference = options.Single(o => o.TryGetProperty("$ref", out _)).GetProperty("$ref").GetString()!;
        JsonElement slotType = schemas.GetProperty(reference["#/components/schemas/".Length..]);
        Assert.Contains(slotType.GetProperty("enum").EnumerateArray(), v => v.ValueKind == JsonValueKind.String && v.GetString() == "Head");
    }
}
