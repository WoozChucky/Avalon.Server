using Scalar.AspNetCore;

namespace Avalon.Api.Hosting;

/// <summary>
/// The API's own documentation, the OpenAPI document at <c>/openapi/v1.json</c> and Scalar at <c>/scalar</c>, served
/// only in Development or where <see cref="EnabledSetting"/> turns it on, never by default in production (#803). The
/// published document does not depend on it: the docs build (<see cref="AvalonApiHost.OpenApiGenerationOnlyVariable"/>)
/// reads the document from the host without serving it.
/// </summary>
public static class ApiDocs
{
    public const string EnabledSetting = "Application:ApiDocs:Enabled";

    /// <summary>Whether a process in <paramref name="environment"/> with <paramref name="configuration"/> serves the docs.</summary>
    public static bool AreServed(IHostEnvironment environment, IConfiguration configuration) =>
        environment.IsDevelopment() || configuration.GetValue<bool>(EnabledSetting);

    /// <summary>Maps the OpenAPI document and Scalar on <paramref name="endpoints"/>.</summary>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapOpenApi();
        endpoints.MapScalarApiReference(options =>
        {
            options.WithTitle("Avalon.Api");
            options.WithTheme(ScalarTheme.BluePlanet);
            options.HideSidebar();
        });
    }
}
