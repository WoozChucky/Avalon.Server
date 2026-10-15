using System.Reflection;
using System.Text;
using System.Text.Json;
using Avalon.Api.Hosting.Controllers;
using Avalon.Api.Identity.Controllers;
using Avalon.Api.Testing;
using Avalon.Common.GameAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.GameAuth;

/// <summary>
/// Every API endpoint that takes an <c>Idempotency-Key</c> answers a request without a usable one with 400
/// <c>IDEMPOTENCY_KEY_REQUIRED</c> before its action runs, rather than as a bad credential (401 <c>INVALID_ATTEMPT</c>
/// before), and passes a usable key, and a plaintext request, on to the action as before.
/// </summary>
public sealed class IdempotencyKeyShould
{
    private const string Header = IdempotencyKeyRequiredAttribute.Header;

    /// <summary>Every controller action of every API service, and of the hosting they share, that binds the header.</summary>
    private static IEnumerable<(Type Controller, MethodInfo Action)> ActionsBindingTheKey() =>
        from assembly in ApiServices.All.Select(service => service.ControllerAssembly).Append(typeof(BaseController).Assembly).Distinct()
        from controller in assembly.GetTypes()
        where typeof(ControllerBase).IsAssignableFrom(controller) && !controller.IsAbstract
        from action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance)
        where action.GetCustomAttribute<NonActionAttribute>() is null && action.GetParameters().Any(BindsTheKey)
        select (controller, action);

    // On the parameter itself, or on a property of a parameter bound as a model.
    private static bool BindsTheKey(ParameterInfo parameter) =>
        IsTheKey(parameter.GetCustomAttribute<FromHeaderAttribute>(), parameter.Name) ||
        parameter.ParameterType.GetProperties()
            .Any(property => IsTheKey(property.GetCustomAttribute<FromHeaderAttribute>(), property.Name));

    // A header's name is compared as HTTP compares it, without regard to case.
    private static bool IsTheKey(FromHeaderAttribute? header, string? name) =>
        header is not null && string.Equals(header.Name ?? name, Header, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Be_required_by_every_action_that_binds_it()
    {
        string[] unguarded = ActionsBindingTheKey()
            .Where(found => found.Action.GetCustomAttribute<IdempotencyKeyRequiredAttribute>() is null)
            .Select(found => $"{found.Controller.Name}.{found.Action.Name}")
            .ToArray();
        Assert.Empty(unguarded);
    }

    /// <summary>The route, method and whether a signed-in caller is needed, of every action that binds the header.</summary>
    public static TheoryData<string, string, bool> Endpoints()
    {
        var endpoints = new TheoryData<string, string, bool>();
        foreach ((Type controller, MethodInfo action) in ActionsBindingTheKey())
        {
            HttpMethodAttribute verb = action.GetCustomAttributes<HttpMethodAttribute>().Single();
            string path = "/" + string.Join('/', new[] { controller.GetCustomAttribute<RouteAttribute>()?.Template, verb.Template }
                .Where(part => !string.IsNullOrEmpty(part)));
            bool signedIn = action.GetCustomAttribute<AllowAnonymousAttribute>() is null &&
                (controller.GetCustomAttribute<AuthorizeAttribute>() ?? action.GetCustomAttribute<AuthorizeAttribute>()) is not null;
            endpoints.Add(path, verb.HttpMethods.Single(), signedIn);
        }

        return endpoints;
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Refuse_a_missing_empty_unparsable_or_zero_key_as_such(string path, string method, bool signedIn)
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync();
        host.AccountNowIs(ApiTestHost.MakeAccount());
        string? bearer = signedIn ? ApiTestHost.Mint(ApiTestHost.MakeAccount()) : null;

        StringValues[] unusable =
        [
            StringValues.Empty, "", "not-a-guid", Guid.Empty.ToString(),
            new([Guid.NewGuid().ToString(), Guid.NewGuid().ToString()]),
        ];
        foreach (StringValues key in unusable)
        {
            HttpContext refused = await SendAsync(host, method, path, "https", key, bearer);
            Assert.Equal(StatusCodes.Status400BadRequest, refused.Response.StatusCode);
            Assert.Equal(GameAuthErrors.IdempotencyKeyRequired, await ErrorAsync(refused));
            // As every other answer on these routes: never stored by a cache.
            Assert.Equal("no-store,no-cache", refused.Response.Headers.CacheControl.ToString());
            Assert.Equal("no-cache", refused.Response.Headers.Pragma.ToString());
        }

        // A usable key reaches the action, and a plaintext request is the action's to refuse as plaintext.
        Assert.NotEqual(GameAuthErrors.IdempotencyKeyRequired,
            await ErrorAsync(await SendAsync(host, method, path, "https", Guid.NewGuid().ToString(), bearer)));
        Assert.NotEqual(GameAuthErrors.IdempotencyKeyRequired,
            await ErrorAsync(await SendAsync(host, method, path, "http", StringValues.Empty, bearer)));
    }

    private static Task<HttpContext> SendAsync(ApiTestHost host, string method, string path, string scheme, StringValues key,
        string? bearer) =>
        ((TestServer)host.Services.GetRequiredService<IServer>()).SendAsync(http =>
        {
            http.Request.Method = method;
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
