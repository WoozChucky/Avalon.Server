using System.Reflection;
using Avalon.Api.Hosting.Worlds;
using Avalon.Api.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Avalon.Api.Hosting.UnitTests.Hosting;

/// <summary>
/// The one API pipeline adds its middleware in a fixed order (#794, design section 3.1), the order Avalon.Api's
/// Program.cs had before the split: every service process runs the same pipeline, with the services' hooks at their
/// two places around authentication, in the services' order.
/// </summary>
public sealed class PipelineOrderShould
{
    [Fact]
    public async Task Add_the_middleware_in_the_fixed_order_with_the_services_hooks_around_authentication()
    {
        List<string> names = [];

        await MiddlewareRecorder.RecordAsync(Environments.Production,
            [new RecordingService("first", names, worldRoutes: true), new RecordingService("second", names)], names);

        Assert.Equal(
        [
            "ExceptionHandlerMiddleware",
            "RequestLoggingMiddleware",
            "ForwardedHeadersSetup",
            "ForwardedHeadersMiddleware",
            "EndpointRoutingMiddleware",
            "CorsMiddleware",
            "first: before authentication",
            "second: before authentication",
            "AuthenticationMiddleware",
            "first: after authentication",
            "second: after authentication",
            "ApiRateLimiting",
            "RateLimitingMiddleware",
            "WorldRouteMiddleware",
            "AuthorizationMiddlewareInternal",
        ], names);
    }

    [Fact]
    public async Task Leave_the_world_routes_out_when_no_service_declares_them()
    {
        List<string> names = [];

        await MiddlewareRecorder.RecordAsync(Environments.Production, [new RecordingService("only", names)], names);

        Assert.DoesNotContain("WorldRouteMiddleware", names);
        Assert.Equal("AuthorizationMiddlewareInternal", names[^1]);
        Assert.Equal("RateLimitingMiddleware", names[^2]);
    }

    [Fact]
    public async Task Put_the_developer_exception_page_first_in_development_only()
    {
        List<string> development = await MiddlewareRecorder.RecordAsync(Environments.Development, [new RecordingService("only", [])]);
        List<string> production = await MiddlewareRecorder.RecordAsync(Environments.Production, [new RecordingService("only", [])]);

        Assert.Equal("DeveloperExceptionPageMiddlewareImpl", development[0]);
        Assert.Equal(development.Skip(1), production);
    }

    /// <summary>A service with no controllers and no needs, whose hooks write where they ran.</summary>
    private sealed class RecordingService(string name, List<string> names, bool worldRoutes = false) : IApiService
    {
        public string Name => name;

        public Assembly ControllerAssembly => typeof(PipelineOrderShould).Assembly;

        public ApiServiceNeeds Needs => new(false, WorldDatabaseParts.None, AuthSchemaRole.Reader, worldRoutes);

        public void AddServices(WebApplicationBuilder builder)
        {
        }

        public void UseBeforeAuthentication(IApplicationBuilder app) =>
            names.Add($"{name}: before authentication");

        public void UseAfterAuthentication(IApplicationBuilder app) =>
            names.Add($"{name}: after authentication");
    }
}
