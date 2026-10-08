using System.Text.Json.Nodes;
using Avalon.Api.Contract;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Avalon.Api.Hosting.UnitTests.Hosting;

/// <summary>
/// A Steam-only account has no email until it adds recovery credentials, so the published document must let
/// <c>AccountDto.email</c> be null, or a client generated from it rejects that account (#825).
/// </summary>
public sealed class AccountDtoOpenApiShould
{
    [Fact]
    public async Task Describe_the_account_email_as_nullable()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication();
        builder.Services.AddAvalonOpenApi();
        await using WebApplication app = builder.Build();
        app.MapGet("/account", () => new AccountDto());
        app.MapOpenApi();
        await app.StartAsync();

        using HttpClient client = app.GetTestClient();
        JsonNode document = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json"))!;
        JsonNode email = document["components"]!["schemas"]![typeof(AccountDto).FullName!]!["properties"]!["email"]!;

        Assert.Contains("\"null\"", email.ToJsonString(), StringComparison.Ordinal);
    }
}
