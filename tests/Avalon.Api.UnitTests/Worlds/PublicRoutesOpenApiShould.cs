using System.Text.Json;
using Xunit;

namespace Avalon.Api.UnitTests.Worlds;

/// <summary>The public tooltip routes the Dashboard's generated client depends on.</summary>
public sealed class PublicRoutesOpenApiShould(OpenApiDocumentFixture fixture) : IClassFixture<OpenApiDocumentFixture>
{
    [Theory]
    [InlineData("/public/world", "ListPublicWorlds")]
    [InlineData("/public/world/{worldId}/item/{id}", "GetPublicItem")]
    [InlineData("/public/world/{worldId}/ability/{id}", "GetPublicAbility")]
    public void Describe_the_public_route(string path, string operationId)
    {
        JsonElement paths = fixture.Document.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty(path, out JsonElement item), $"{path} missing");
        Assert.Equal(operationId, item.GetProperty("get").GetProperty("operationId").GetString());
    }
}
