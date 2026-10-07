using System.Reflection;
using Avalon.Api.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Avalon.Api.Testing;

/// <summary>
/// Records the middleware the API pipeline adds (<see cref="ApiPipeline.Use"/>), in order, by name (#794): a class
/// middleware by its type, a lambda by the type that declares it. Calls go on to the application it wraps, so routing,
/// CORS and the rest see the application they expect; nothing is started.
/// </summary>
public sealed class MiddlewareRecorder(IApplicationBuilder inner, List<string> names) : IApplicationBuilder
{
    public IServiceProvider ApplicationServices
    {
        get => inner.ApplicationServices;
        set => inner.ApplicationServices = value;
    }

    public IFeatureCollection ServerFeatures => inner.ServerFeatures;

    public IDictionary<string, object?> Properties => inner.Properties;

    public IApplicationBuilder Use(Func<RequestDelegate, RequestDelegate> middleware)
    {
        names.Add(NameOf(middleware));
        inner.Use(middleware);
        return this;
    }

    public IApplicationBuilder New() => inner.New();

    public RequestDelegate Build() => inner.Build();

    /// <summary>
    /// The pipeline <see cref="ApiPipeline.Use"/> adds for <paramref name="services"/> in
    /// <paramref name="environment"/>, built on <see cref="AvalonApiHost.CreateBuilder(WebApplicationOptions, IReadOnlyList{IApiService}, Action{WebApplicationBuilder}?)"/>,
    /// with each service's hooks recorded where they ran when <paramref name="names"/> is the list they write to.
    /// </summary>
    public static async Task<List<string>> RecordAsync(string environment, IReadOnlyList<IApiService> services,
        List<string>? names = null)
    {
        names ??= [];
        WebApplicationBuilder builder = AvalonApiHost.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = environment }, services, configure: b =>
            {
                b.WebHost.UseTestServer();
                b.Logging.ClearProviders();
                b.Configuration.AddInMemoryCollection(ApiTestHost.SettingsFor(services));
            });
        await using WebApplication app = builder.Build();
        ApiPipeline.Use(new MiddlewareRecorder(app, names), app, app.Environment, services);
        return names;
    }

    private static string NameOf(Func<RequestDelegate, RequestDelegate> middleware)
    {
        // UseMiddleware<T> adds its binder's CreateMiddleware, and the binder names the middleware type.
        if (middleware.Method.Name == "CreateMiddleware" && middleware.Target?.ToString() is { } type)
            return type[(type.LastIndexOfAny(['.', '+']) + 1)..];

        // A lambda, wrapped by the Use extension in a closure that holds it: the type declaring the lambda.
        object? target = middleware.Target;
        Delegate? lambda = target?.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(field => field.GetValue(target))
            .OfType<Delegate>()
            .FirstOrDefault();
        Type? declaring = (lambda ?? middleware).Method.DeclaringType;
        while (declaring is not null && declaring.Name.Contains('<', StringComparison.Ordinal))
            declaring = declaring.DeclaringType;
        return declaring?.Name ?? middleware.Method.Name;
    }
}
