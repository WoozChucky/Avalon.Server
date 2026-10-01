using System.Reflection;
using System.Text.Json;
using Avalon.Api.Contract;
using Avalon.Api.Templates;
using Avalon.Domain.World;
using Xunit;

namespace Avalon.Api.UnitTests.Worlds;

/// <summary>
/// #745: the creature <c>AIName</c> and <c>RespawnTimerSecs</c> columns were never read by the world, so the
/// creature DTO, the update request, the editable fields and the OpenAPI document no longer carry them.
/// </summary>
public sealed class CreatureTemplateOpenApiShould(OpenApiDocumentFixture fixture) : IClassFixture<OpenApiDocumentFixture>
{
    private static readonly string[] Dropped = ["aiName", "respawnTimerSecs"];

    [Fact]
    public void Leave_the_dropped_columns_out_of_the_dto_and_the_request()
    {
        foreach (Type type in new[] { typeof(CreatureTemplateDto), typeof(UpdateCreatureTemplateRequest) })
        {
            string[] names = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.Name.ToLowerInvariant()).ToArray();
            foreach (string dropped in Dropped)
                Assert.DoesNotContain(dropped.ToLowerInvariant(), names);
        }
    }

    [Fact]
    public void Leave_the_dropped_columns_out_of_the_version_hash()
    {
        // The editable-field list (TemplateFields.Creature) is internal and assigns request properties
        // that the test above shows are gone; the version hash covers the entity's columns.
        string[] columns = TemplateVersion.ColumnsOf(typeof(CreatureTemplate)).Select(c => c.ToLowerInvariant()).ToArray();
        foreach (string dropped in Dropped)
            Assert.DoesNotContain(dropped.ToLowerInvariant(), columns);
        Assert.Contains("bodyremovetimersecs", columns);
        Assert.Contains("scriptname", columns);
    }

    [Fact]
    public void Leave_the_dropped_columns_out_of_the_openapi_document()
    {
        JsonElement schemas = fixture.Document.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (string schema in new[] { "Avalon.Api.Contract.CreatureTemplateDto", "Avalon.Api.Contract.UpdateCreatureTemplateRequest" })
        {
            JsonElement properties = schemas.GetProperty(schema).GetProperty("properties");
            foreach (string dropped in Dropped)
                Assert.False(properties.TryGetProperty(dropped, out _), $"{schema} still has {dropped}");
            Assert.True(properties.TryGetProperty("bodyRemoveTimerSecs", out _));
        }
    }
}
