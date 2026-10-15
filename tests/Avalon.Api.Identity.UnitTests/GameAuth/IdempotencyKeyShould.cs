using System.Reflection;
using System.Text;
using System.Text.Json;
using Avalon.Api.Identity.Controllers;
using Avalon.Api.Testing;
using Avalon.Common.GameAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.GameAuth;

/// <summary>
/// Every identity endpoint that takes an <c>Idempotency-Key</c> answers a request without a usable one with 400
/// <c>IDEMPOTENCY_KEY_REQUIRED</c> before its action runs, rather than as a bad credential (401 <c>INVALID_ATTEMPT</c>
/// before), and passes a usable key, and a plaintext request, on to the action as before.
/// </summary>
public sealed class IdempotencyKeyShould
{
    private const string Header = "Idempotency-Key";

    /// <summary>Every identity action that binds the header, so an endpoint added later is held to the rule too.</summary>
    public static TheoryData<string, bool> Endpoints()
    {
        var endpoints = new TheoryData<string, bool>();
        foreach (Type controller in typeof(GameAuthController).Assembly.GetTypes()
                     .Where(type => typeof(ControllerBase).IsAssignableFrom(type)))
        {
            foreach (MethodInfo action in controller.GetMethods())
            {
                if (!action.GetParameters().Any(parameter => parameter.GetCustomAttribute<FromHeaderAttribute>()?.Name == Header))
                    continue;
                string route = controller.GetCustomAttribute<RouteAttribute>()!.Template;
                string template = action.GetCustomAttribute<HttpPostAttribute>()!.Template!;
                endpoints.Add($"/{route}/{template}", controller.GetCustomAttribute<AuthorizeAttribute>() is not null);
            }
        }

        return endpoints;
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Refuse_a_missing_empty_unparsable_or_zero_key_as_such(string path, bool signedIn)
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync([IdentityApi.Service]);
        host.AccountNowIs(ApiTestHost.MakeAccount());
        string? bearer = signedIn ? ApiTestHost.Mint(ApiTestHost.MakeAccount()) : null;

        StringValues[] unusable =
        [
            StringValues.Empty, "", "not-a-guid", Guid.Empty.ToString(),
            new([Guid.NewGuid().ToString(), Guid.NewGuid().ToString()]),
        ];
        foreach (StringValues key in unusable)
        {
            HttpContext refused = await PostAsync(host, path, "https", key, bearer);
            Assert.Equal(StatusCodes.Status400BadRequest, refused.Response.StatusCode);
            Assert.Equal(GameAuthErrors.IdempotencyKeyRequired, await ErrorAsync(refused));
        }

        // A usable key reaches the action, and a plaintext request is the action's to refuse as plaintext.
        Assert.NotEqual(GameAuthErrors.IdempotencyKeyRequired,
            await ErrorAsync(await PostAsync(host, path, "https", Guid.NewGuid().ToString(), bearer)));
        Assert.NotEqual(GameAuthErrors.IdempotencyKeyRequired,
            await ErrorAsync(await PostAsync(host, path, "http", StringValues.Empty, bearer)));
    }

    private static Task<HttpContext> PostAsync(ApiTestHost host, string path, string scheme, StringValues key, string? bearer) =>
        ((TestServer)host.Services.GetRequiredService<IServer>()).SendAsync(http =>
        {
            http.Request.Method = HttpMethods.Post;
            http.Request.Scheme = scheme;
            http.Request.Path = path;
            http.Request.ContentType = "application/json";
            http.Request.Body = new MemoryStream("{}"u8.ToArray());
            if (key.Count != 0) http.Request.Headers[Header] = key;
            if (bearer is not null) http.Request.Headers.Authorization = $"Bearer {bearer}";
        });

    /// <summary>The <c>error</c> of the reply, or null when it has none.</summary>
    private static async Task<string?> ErrorAsync(HttpContext context)
    {
        string body = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();
        if (body.Length == 0) return null;
        using var json = JsonDocument.Parse(body);
        return json.RootElement.ValueKind == JsonValueKind.Object &&
               json.RootElement.TryGetProperty("error", out JsonElement error) && error.ValueKind == JsonValueKind.String
            ? error.GetString()
            : null;
    }
}
