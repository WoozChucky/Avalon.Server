using System.Text.Json;
using Avalon.Api.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Avalon.Api.UnitTests.Worlds;

/// <summary>The generated OpenAPI document, which the Dashboard's client is generated from (#523, Dashboard #270).</summary>
public sealed class OpenApiDocumentFixture : IAsyncLifetime
{
    private WebApplication _app = null!;
    public JsonDocument Document { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(CharacterController).Assembly);
        // As Program.cs names schemas, so types that share a short name do not collide.
        builder.Services.AddOpenApi(options => options.CreateSchemaReferenceId = type => type.Type.FullName!);
        _app = builder.Build();
        _app.MapOpenApi();
        _app.MapControllers();
        await _app.StartAsync();
        using HttpClient client = _app.GetTestClient();
        Document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
    }

    public async Task DisposeAsync()
    {
        Document.Dispose();
        await _app.DisposeAsync();
    }
}

public sealed class WorldRoutesOpenApiShould(OpenApiDocumentFixture fixture) : IClassFixture<OpenApiDocumentFixture>
{
    public static TheoryData<string, string> MovedRoutes => new()
    {
        { "/world/{worldId}/character/paginate", "get" },
        { "/world/{worldId}/character/{id}", "get" },
        { "/world/{worldId}/character/{id}", "patch" },
        { "/world/{worldId}/character/{id}/inventory", "get" },
        { "/world/{worldId}/character/{id}/abilities", "get" },
        { "/world/{worldId}/map-template", "get" },
        { "/world/{worldId}/map-template/{id}", "get" },
        { "/world/{worldId}/map-template/{id}/preview-layout", "get" },
        { "/world/{worldId}/map-template/chunk-asset/{filename}", "get" },
        { "/world/{worldId}/ability-template", "get" },
        { "/world/{worldId}/ability-template/{id}", "get" },
        { "/world/{worldId}/item-template", "get" },
        { "/world/{worldId}/item-template/{id}", "get" },
        { "/world/{worldId}/creature-template", "get" },
        { "/world/{worldId}/creature-template/{id}", "get" },
        { "/world/{worldId}/observability/character/{id}", "get" },
    };

    private JsonElement Paths => fixture.Document.RootElement.GetProperty("paths");

    [Theory]
    [MemberData(nameof(MovedRoutes))]
    public void Mark_worldId_a_required_path_parameter(string path, string method)
    {
        Assert.True(Paths.TryGetProperty(path, out JsonElement item),
            $"{path} missing; the document has: {string.Join(", ", Paths.EnumerateObject().Select(p => p.Name))}");
        JsonElement worldId = item.GetProperty(method).GetProperty("parameters").EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == "worldId");

        Assert.Equal("path", worldId.GetProperty("in").GetString());
        Assert.True(worldId.GetProperty("required").GetBoolean());
    }

    [Fact]
    public void Leave_no_world_content_route_outside_a_world()
    {
        string[] unscoped = ["/character/", "/map-template", "/item-template", "/ability-template", "/creature-template",
            "/observability/character"];

        Assert.DoesNotContain(Paths.EnumerateObject(), p => unscoped.Any(prefix => p.Name.StartsWith(prefix, StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("/character")]
    [InlineData("/world")]
    [InlineData("/world/{id}")]
    [InlineData("/observability/online")]
    [InlineData("/observability/instance/{instanceId}")]
    public void Keep_the_cross_world_routes_unscoped(string path)
    {
        JsonElement get = Paths.GetProperty(path).GetProperty("get");

        Assert.False(get.TryGetProperty("parameters", out JsonElement parameters)
                     && parameters.EnumerateArray().Any(p => p.GetProperty("name").GetString() == "worldId"));
    }
}
