using System.Reflection;

namespace Avalon.Api.Hosting;

/// <summary>
/// One API service (#794, design section 2.2), as the shared host runs it: its name, the assembly that holds its
/// controllers, what it needs, and the hooks through which it adds to the host. <see cref="AvalonApiHost"/> calls
/// them in this order: <see cref="ConfigureBuilder"/>, before the logging; <see cref="AddServices"/>, after the shared
/// registrations; the two middleware hooks, at their fixed places in the one pipeline (<see cref="ApiPipeline"/>);
/// <see cref="LogStartup"/>, once the host is built; and <see cref="StartAsync"/>, once the startup checks and the
/// databases have passed. A hook a service does not need is left as it is.
/// </summary>
public interface IApiService
{
    /// <summary>The service's name, as <c>Application:Services</c> names it.</summary>
    string Name { get; }

    /// <summary>The assembly that holds the service's controllers; MVC discovers controllers there only.</summary>
    Assembly ControllerAssembly { get; }

    ApiServiceNeeds Needs { get; }

    /// <summary>Configures the builder itself, before anything is registered: a Kestrel listener of its own, say.</summary>
    void ConfigureBuilder(WebApplicationBuilder builder)
    {
    }

    /// <summary>Registers the service's options, services, schemes, policies and mappers.</summary>
    void AddServices(WebApplicationBuilder builder);

    /// <summary>Adds middleware after routing and CORS, before authentication.</summary>
    void UseBeforeAuthentication(IApplicationBuilder app)
    {
    }

    /// <summary>Adds middleware after authentication, before the rate limiter.</summary>
    void UseAfterAuthentication(IApplicationBuilder app)
    {
    }

    /// <summary>Writes the service's startup lines, once the host is built, also when the host only generates the OpenAPI document.</summary>
    void LogStartup(IServiceProvider services, ILogger logger)
    {
    }

    /// <summary>
    /// The service's startup work once the options, its checks, the auth schema and the world databases have passed,
    /// before the Redis connection is made and the host serves. OpenAPI generation skips it.
    /// </summary>
    Task StartAsync(IServiceProvider services, CancellationToken cancellationToken) => Task.CompletedTask;
}
