using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Avalon.Api.Hosting.UnitTests.Hosting;

/// <summary>
/// The document an API process serves lists its paths, tags and schemas in ordinal order (#794, design section 3.1),
/// whichever order its endpoints were mapped in, so it does not depend on which assembly holds a controller.
/// </summary>
public sealed class OpenApiOrderShould
{
    [Fact]
    public async Task List_paths_tags_and_schemas_in_ordinal_order_whatever_the_mapping_order()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication();
        builder.Services.AddAvalonOpenApi();
        await using WebApplication app = builder.Build();
        app.MapGet("/zeta", () => new Zeta(1)).WithTags("Zeta");
        app.MapGet("/beta", () => new Beta(2)).WithTags("Beta");
        app.MapGet("/alpha", () => new Alpha("a")).WithTags("Alpha");
        app.MapOpenApi();
        await app.StartAsync();

        using HttpClient client = app.GetTestClient();
        JsonNode document = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json"))!;

        Assert.Equal(["/alpha", "/beta", "/zeta"], Keys(document["paths"]));
        Assert.Equal(["Alpha", "Beta", "Zeta"], document["tags"]!.AsArray().Select(tag => (string)tag!["name"]!));
        List<string> schemas = Keys(document["components"]!["schemas"]);
        Assert.Equal(schemas.Order(StringComparer.Ordinal), schemas);
        Assert.Contains(typeof(Zeta).FullName, schemas);
        Assert.Contains(typeof(Alpha).FullName, schemas);
    }

    private static List<string> Keys(JsonNode? node) => node!.AsObject().Select(member => member.Key).ToList();

    private sealed record Zeta(int Value);

    private sealed record Beta(int Value);

    private sealed record Alpha(string Value);
}
